using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The badge surfaces of one combat: strips over hand cards, the End Turn display,
/// and the top-center panel — with placement and multiplicity steered live by the
/// ViewOptions switches. Created when the combat UI readies, disposed when it
/// leaves.
///
/// Everything is event-driven off the same signals the native UI repaints on —
/// relic DisplayAmountChanged/StatusChanged for counts, HoveredModelTracker for the
/// focused card (which both mouse hover and controller focus feed), CombatManager
/// for lifecycle, the coalesced CombatStateChanged as the catch-all. No polling,
/// no per-frame work.
/// </summary>
internal sealed class CombatOverlayView : IDisposable
{
    // Hand strip bands, in holder-local space (origin = card center, card spans
    // y -211..211, the native %HandIndex hotkey digit occupies y -288..-224):
    // - Default: bottom edge fixed just above the digit, backing grows upward.
    // - CardOverlay: centered on Hypergeo's proven on-art band (center y -88).
    // - ReplaceCardCount: dropped onto the digit's own spot — the band's bottom
    //   matches the digit's so nothing peeks out — which the strip absorbs as a
    //   "N. " prefix while the digit itself is hidden.
    // Bands scale with the badge scale (Big & Bold): the fixed edge stays put and
    // the band grows away from the card. Only the icon row scales — the tooltip
    // backing's content insets are fixed — so the band is row + insets, not a
    // flat multiple (which left the panel one-third empty at double size).
    private const float BadgeRowHeight = 40f;
    private const float BackingInsets = 44f;
    private const float HandStripBottom = -264f;
    private const float OverlayCenter = -88f;
    private const float ReplaceCountBottom = -224f;
    // Wide enough that a many-badge panel still has free space to center in; an
    // HBox only centers within free space, and a panel wider than the band used
    // to pin left and read as off-center.
    private const float HandStripHalfWidth = 400f;

    // The End Turn button root is 220x90; this band hugs its top edge (the
    // multiplayer vote row at y -15..15 only exists in co-op, and drawing over it
    // was accepted in exchange for proximity). The replacement face is the
    // ReplaceEndTurn variant: the native Visuals hide entirely and a taller backed
    // face spans the badge row plus the End Turn text over the same click region.
    // Wide band, same center as the button (108.5), so a long badge row centers
    // instead of pinning left.
    private const float EndTurnStripLeft = -141f;
    private const float EndTurnStripRight = 358f;
    private const float EndTurnStripTop = -46f;
    private const float EndTurnStripBottom = -6f;
    // Wider than the button (same center, 110) and as short as its content: badge
    // rows of at most four wrap, every interactive cell stays above the button's
    // click region (y < 0), and only the mouse-ignoring label dips onto the art.
    private const float FaceLeft = -70f;
    private const float FaceRight = 290f;
    private const float FaceLabelDip = 46f;
    private const int FaceBadgesPerRow = 4;
    private const float FaceRowHeight = 46f;
    private const float FaceLabelHeight = 36f;

    // While a card is held for play, the targeting arrow (GlobalUi, z of its source
    // control + 1) would draw over the strip. This absolute z outranks it, and is
    // applied only in that state so the strip can never punch through the overlay
    // screens during normal hover.
    private const int InPlayZ = 50;

    // The top-center panel sits at 20% down the screen ("80% of the way up"); its
    // band height is computed from the badge scale on every refresh, like the
    // hand strips, so the backing hugs the icons at either size.
    private const float TopPanelAnchorY = 0.2f;

    private readonly NCombatUi _ui;
    private readonly List<RelicModel> _subscribedRelics = [];
    private readonly Dictionary<NHandCardHolder, HBoxContainer> _handStrips = [];
    private readonly Dictionary<NHandCardHolder, Label> _hiddenIndexLabels = [];
    private readonly OptionsPanel _options = new();
    private HBoxContainer? _endTurnStrip;
    private HBoxContainer? _endTurnExtension;
    private HBoxContainer? _topPanel;
    private MegaLabel? _faceLabel;
    private Player? _player;

    public CombatOverlayView(NCombatUi ui) => _ui = ui;

    public void Attach()
    {
        _endTurnStrip = BadgeStrip.CreateContainer("MicroAssistantEndTurnStrip");
        // As a child of the button it inherits the show/hide tweens, including the
        // slide out on enemy turns. The hover animation moves only the button's
        // Visuals child, so the strip does not jitter with it.
        _ui.EndTurnButton.AddChild(_endTurnStrip);

        _endTurnExtension = BadgeStrip.CreateContainer("MicroAssistantEndTurnFace");
        _endTurnExtension.OffsetLeft = FaceLeft;
        _endTurnExtension.OffsetRight = FaceRight;
        _ui.EndTurnButton.AddChild(_endTurnExtension);
        // Mirror the native label highlight: the button still takes the hover (all
        // face parts over its rect ignore the mouse), we just repaint our text.
        _ui.EndTurnButton.MouseEntered += OnEndTurnHoverStart;
        _ui.EndTurnButton.MouseExited += OnEndTurnHoverEnd;

        // Full-width so its centered content needs no measuring; only visible while
        // a card is focused or in play, which is also why its absolute z (over the
        // targeting arrow) is safe to leave on permanently.
        _topPanel = BadgeStrip.CreateTriggerBanner("MicroAssistantTopPanel");
        _topPanel.AnchorLeft = 0f;
        _topPanel.AnchorRight = 1f;
        _topPanel.AnchorTop = TopPanelAnchorY;
        _topPanel.AnchorBottom = TopPanelAnchorY;
        _topPanel.ZAsRelative = false;
        _topPanel.ZIndex = InPlayZ;
        _ui.AddChild(_topPanel);

        Guard.Run("Attaching the view options panel", () =>
        {
            if (NRun.Instance != null)
                _options.Attach(NRun.Instance.GlobalUi, StatusLine);
        });

        CombatManager.Instance.CombatBegan += OnCombatBegan;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        CombatManager.Instance.TurnStarted += OnTurnChanged;
        CombatManager.Instance.TurnEnded += OnTurnChanged;
        RunManager.Instance.HoveredModelTracker.HoverChanged += OnHoverChanged;
        // Powers have no per-instance events worth wiring; the tracker's coalesced
        // once-per-frame signal covers power changes, history ticks, and anything
        // else the specific subscriptions above miss.
        CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;
        ViewOptions.Changed += OnOptionsChanged;

        // The combat usually begins after the UI readies, but bind now in case this
        // view arrives mid-combat.
        if (CombatManager.Instance.IsInProgress)
            BindPlayer(CombatManager.Instance.DebugOnlyGetState());
    }

    public void Dispose()
    {
        CombatManager.Instance.CombatBegan -= OnCombatBegan;
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        CombatManager.Instance.TurnStarted -= OnTurnChanged;
        CombatManager.Instance.TurnEnded -= OnTurnChanged;
        RunManager.Instance.HoveredModelTracker.HoverChanged -= OnHoverChanged;
        CombatManager.Instance.StateTracker.CombatStateChanged -= OnCombatStateChanged;
        ViewOptions.Changed -= OnOptionsChanged;
        UnbindPlayer();
        RestoreIndexLabels();
        foreach (var strip in _handStrips.Values)
            if (GodotObject.IsInstanceValid(strip))
                strip.QueueFree();
        _handStrips.Clear();
        if (_ui.EndTurnButton != null && GodotObject.IsInstanceValid(_ui.EndTurnButton))
        {
            _ui.EndTurnButton.MouseEntered -= OnEndTurnHoverStart;
            _ui.EndTurnButton.MouseExited -= OnEndTurnHoverEnd;
            // The native button face must come back even if the flag is still on.
            var visuals = _ui.EndTurnButton.GetNodeOrNull<Control>("Visuals");
            if (visuals != null && GodotObject.IsInstanceValid(visuals))
                visuals.Visible = true;
        }
        FreeStrip(ref _endTurnStrip);
        FreeStrip(ref _endTurnExtension);
        FreeStrip(ref _topPanel);
        _options.Dispose();
    }

    /// <summary>Resync now; for patches that fire on transitions no event covers.</summary>
    public void SyncHandStripNow() => SyncHandStrips();

    private void OnCombatBegan(CombatState state) =>
        Guard.Run("Binding the overlay to the new combat", () => BindPlayer(state));

    private void OnCombatEnded(CombatRoom _) =>
        Guard.Run("Unbinding the overlay from the ended combat", () =>
        {
            UnbindPlayer();
            RefreshEndTurn();
            SyncHandStrips();
        });

    private void OnTurnChanged(CombatState _) =>
        Guard.Run("Refreshing the End Turn badges on turn change", RefreshEndTurn);

    private void OnHoverChanged(ulong _) =>
        Guard.Run("Following the focused hand card", () =>
        {
            Diag.Once("hover", () => "Hover event received.");
            SyncHandStrips();
        });

    private void OnCombatStateChanged(CombatState _) =>
        Guard.Run("Refreshing badges on combat state change", () =>
        {
            Diag.Once("statechange", () => "Combat state change received.");
            RefreshEndTurn();
            SyncHandStrips();
        });

    private void OnOptionsChanged() =>
        Guard.Run("Applying changed view options", () =>
        {
            RestoreIndexLabels();
            RefreshEndTurn();
            SyncHandStrips();
        });

    /// <summary>A tracked relic's counter or status moved; every surface may care.</summary>
    private void OnRelicChanged() =>
        Guard.Run("Refreshing badges on a relic change", () =>
        {
            RefreshEndTurn();
            SyncHandStrips();
        });

    private void OnRelicListChanged(RelicModel _) =>
        Guard.Run("Rewiring badges after a relic was gained or lost", () =>
        {
            ResubscribeRelics();
            RefreshEndTurn();
            SyncHandStrips();
        });

    private void BindPlayer(ICombatState? state)
    {
        UnbindPlayer();
        _player = LocalContext.GetMe(state);
        Diag.Once("bind", () => _player == null
            ? "Combat began but the local player was NOT resolved; End Turn badges stay empty."
            : $"Bound to combat: {CountingTracker.TrackedRelics(_player).Count()} tracked relic(s) " +
              $"among {_player.Relics.Count}.");
        if (_player == null)
            return;
        _player.RelicObtained += OnRelicListChanged;
        _player.RelicRemoved += OnRelicListChanged;
        ResubscribeRelics();
        RefreshEndTurn();
    }

    private void UnbindPlayer()
    {
        foreach (var relic in _subscribedRelics)
        {
            relic.DisplayAmountChanged -= OnRelicChanged;
            relic.StatusChanged -= OnRelicChanged;
        }
        _subscribedRelics.Clear();
        if (_player == null)
            return;
        _player.RelicObtained -= OnRelicListChanged;
        _player.RelicRemoved -= OnRelicListChanged;
        _player = null;
    }

    private void ResubscribeRelics()
    {
        foreach (var relic in _subscribedRelics)
        {
            relic.DisplayAmountChanged -= OnRelicChanged;
            relic.StatusChanged -= OnRelicChanged;
        }
        _subscribedRelics.Clear();
        if (_player == null)
            return;
        foreach (var relic in CountingTracker.TrackedRelics(_player))
        {
            relic.DisplayAmountChanged += OnRelicChanged;
            relic.StatusChanged += OnRelicChanged;
            _subscribedRelics.Add(relic);
        }
    }

    private void RefreshEndTurn()
    {
        var badges = _player != null && CombatManager.Instance.IsInProgress
            ? CountingTracker.ForEndTurn(_player)
            : [];
        // "Hide Negated Payoffs": a lost payoff disappears instead of being slashed.
        if (ViewOptions.HideNegatedPayoffs)
            badges = badges.Where(badge => !badge.Broken).ToList();
        Diag.Once("endturn", () =>
            $"End Turn strip refreshed: {badges.Count} badge(s), " +
            $"player {(_player == null ? "unbound" : "bound")}.");
        if (_endTurnStrip != null && GodotObject.IsInstanceValid(_endTurnStrip))
        {
            _endTurnStrip.OffsetLeft = EndTurnStripLeft;
            _endTurnStrip.OffsetRight = EndTurnStripRight;
            _endTurnStrip.OffsetTop = EndTurnStripTop;
            _endTurnStrip.OffsetBottom = EndTurnStripBottom;
            BadgeStrip.SetBadges(
                _endTurnStrip, ViewOptions.ReplaceEndTurn ? [] : badges);
        }
        RefreshEndTurnFace(badges);
    }

    /// <summary>
    /// The Replace End Turn Button face: the native Visuals (art, label, glow) hide
    /// entirely and a backed face carries badge rows — at most four per row,
    /// wrapping, so a long relic list grows down instead of out into the pile
    /// buttons — over the End Turn text, read live from the hidden native label.
    /// The face is sized to its content, every tooltip cell sits above the button's
    /// click region, everything over the button ignores the mouse, and the native
    /// long-press bar is pulled in front of the backing so it stays visible.
    /// </summary>
    private void RefreshEndTurnFace(List<SourceBadge> badges)
    {
        if (_endTurnExtension == null || !GodotObject.IsInstanceValid(_endTurnExtension))
            return;
        var visuals = _ui.EndTurnButton.GetNodeOrNull<Control>("Visuals");
        if (visuals != null)
            visuals.Visible = !ViewOptions.ReplaceEndTurn;

        foreach (var child in _endTurnExtension.GetChildren())
        {
            _endTurnExtension.RemoveChild(child);
            child.QueueFree();
        }
        _faceLabel = null;
        if (!ViewOptions.ReplaceEndTurn)
            return;

        var rowCount = (badges.Count + FaceBadgesPerRow - 1) / FaceBadgesPerRow;
        _endTurnExtension.OffsetBottom = FaceLabelDip;
        _endTurnExtension.OffsetTop = FaceLabelDip -
            (BackingInsets + rowCount * FaceRowHeight + FaceLabelHeight);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 2);
        for (var start = 0; start < badges.Count; start += FaceBadgesPerRow)
        {
            var row = BadgeStrip.CreateContainer("Badges");
            BadgeStrip.SetBadges(
                row, badges.Skip(start).Take(FaceBadgesPerRow).ToList());
            column.AddChild(row);
        }
        var native = _ui.EndTurnButton.GetNodeOrNull<Label>("Visuals/Label");
        _faceLabel = new MegaLabel
        {
            Text = native?.Text ?? "End Turn",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
        };
        _faceLabel.AddThemeFontOverride("font",
            ResourceLoader.Load<Font>("res://themes/kreon_bold_glyph_space_one.tres"));
        _faceLabel.AddThemeFontSizeOverride("font_size", 28);
        _faceLabel.AddThemeColorOverride("font_color", StsColors.cream);
        _faceLabel.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10208f, 0.0816f));
        _faceLabel.AddThemeConstantOverride("outline_size", 10);
        column.AddChild(_faceLabel);
        _endTurnExtension.AddChild(BadgeStrip.BackedPanel(column));
        // The native long-press bar is an earlier sibling and would draw under the
        // backing; keep the gamepad hold feedback on top.
        _ui.EndTurnButton.GetNodeOrNull<Control>("%Bar")?.MoveToFront();
    }

    private void OnEndTurnHoverStart() =>
        Guard.Run("Highlighting the End Turn face", () =>
        {
            if (_faceLabel != null && GodotObject.IsInstanceValid(_faceLabel))
                _faceLabel.Modulate = StsColors.red;
        });

    private void OnEndTurnHoverEnd() =>
        Guard.Run("Unhighlighting the End Turn face", () =>
        {
            if (_faceLabel != null && GodotObject.IsInstanceValid(_faceLabel))
                _faceLabel.Modulate = Colors.White;
        });

    /// <summary>
    /// The button tweens itself off-screen between turns, but our badge bands rise
    /// above its top edge and would peek over the screen bottom; fade them with it.
    /// Driven by postfixes on the button's own AnimOut/AnimIn.
    /// </summary>
    public void SetEndTurnBadgesVisible(bool visible)
    {
        FadeStrip(_endTurnStrip, visible);
        FadeStrip(_endTurnExtension, visible);
    }

    private static void FadeStrip(Control? strip, bool visible)
    {
        if (strip == null || !GodotObject.IsInstanceValid(strip))
            return;
        strip.CreateTween().TweenProperty(strip, "modulate:a", visible ? 1f : 0f, 0.25f);
    }

    /// <summary>
    /// Reconcile the per-holder strips with what deserves one now: every active
    /// holder under Always On, else just the focused card — plus, so the
    /// information survives dragging a card out to play it, the holder of the card
    /// play in progress. In-play wins focus disputes because at the moment a play
    /// starts the old holder may still briefly hold focus.
    /// </summary>
    private void SyncHandStrips()
    {
        var wanted = new List<NHandCardHolder>();
        NHandCardHolder? inPlay = null;
        if (CombatManager.Instance.IsInProgress && NPlayerHand.Instance is { } hand)
        {
            inPlay = InPlayHolder();
            if (ViewOptions.AlwaysOn)
                foreach (var holder in hand.ActiveHolders)
                    wanted.Add(holder);
            var active = inPlay ?? hand.FocusedHolder;
            if (active != null && !wanted.Contains(active))
                wanted.Add(active);
            // Show Up Top moves the active card's display to the top panel; without
            // Always On its above-card strip disappears entirely.
            if (ViewOptions.ShowUpTop && !ViewOptions.AlwaysOn && active != null)
                wanted.Remove(active);
        }

        foreach (var (holder, strip) in _handStrips.Where(pair =>
                     !GodotObject.IsInstanceValid(pair.Key) ||
                     !wanted.Contains(pair.Key)).ToList())
        {
            RestoreIndexLabel(holder);
            if (GodotObject.IsInstanceValid(strip))
                strip.QueueFree();
            _handStrips.Remove(holder);
        }

        foreach (var holder in wanted)
            RefreshHolderStrip(holder, projected: holder == inPlay);

        UpdateTopPanel(inPlay);
    }

    private void RefreshHolderStrip(NHandCardHolder holder, bool projected)
    {
        if (!GodotObject.IsInstanceValid(holder))
            return;
        if (!_handStrips.TryGetValue(holder, out var strip) ||
            !GodotObject.IsInstanceValid(strip))
        {
            // Parented to the holder, a strip rides the card's motion (fan, pop-up,
            // drag) for free and is freed with it.
            strip = BadgeStrip.CreateContainer("MicroAssistantHandStrip");
            strip.OffsetLeft = -HandStripHalfWidth;
            strip.OffsetRight = HandStripHalfWidth;
            holder.AddChild(strip);
            _handStrips[holder] = strip;
        }

        // The scale slider sizes only the active card's display, and only while
        // that display lives on the card (Show Up Top sizes the top panel instead).
        var active = projected ||
            holder == NPlayerHand.Instance?.FocusedHolder;
        var scale = active && !ViewOptions.ShowUpTop ? ViewOptions.Scale : 1f;
        var height = BadgeRowHeight * scale + BackingInsets;
        var (top, bottom) = ViewOptions.CardOverlay
            ? (OverlayCenter - height / 2f, OverlayCenter + height / 2f)
            : ViewOptions.ReplaceCardCount
                ? (ReplaceCountBottom - height, ReplaceCountBottom)
                : (HandStripBottom - height, HandStripBottom);
        strip.OffsetTop = top;
        strip.OffsetBottom = bottom;
        strip.ZAsRelative = !projected;
        strip.ZIndex = projected ? InPlayZ : 0;

        var card = holder.CardModel;
        var badges = card != null
            ? CountingTracker.ForCardPlay(card.Owner, card, projected)
            : [];
        Diag.Once("hand", () =>
            $"Hand strip refreshed: {badges.Count} badge(s) for " +
            $"{card?.Id.Entry ?? "no card"}.");
        BadgeStrip.SetBadges(
            strip, badges, backed: true, scale: scale, prefix: CountPrefix(holder));
        SyncIndexLabel(holder, hide: ViewOptions.ReplaceCardCount && badges.Count > 0);
    }

    /// <summary>
    /// The top-center panel: under Show Up Top it carries the active card's full
    /// badge row (sized by Big &amp; Bold, never showing the card number — that
    /// context is worthless up there) whenever a card is focused or in play;
    /// otherwise it shows the icons of what a play in progress will set off.
    /// </summary>
    private void UpdateTopPanel(NHandCardHolder? inPlay)
    {
        if (_topPanel == null || !GodotObject.IsInstanceValid(_topPanel))
            return;
        var active = inPlay ?? (CombatManager.Instance.IsInProgress
            ? NPlayerHand.Instance?.FocusedHolder
            : null);
        var card = active != null && GodotObject.IsInstanceValid(active)
            ? active.CardModel
            : null;

        if (!ViewOptions.ShowUpTop)
        {
            SizeTopPanel(1f);
            var triggering = card != null && inPlay != null
                ? CountingTracker.ForCardPlay(card.Owner, card, projected: true)
                    .Where(badge => badge.WillTrigger).ToList()
                : [];
            BadgeStrip.SetTriggerIcons(_topPanel, triggering);
            return;
        }

        var scale = ViewOptions.Scale;
        SizeTopPanel(scale);
        var badges = card != null
            ? CountingTracker.ForCardPlay(card.Owner, card, projected: inPlay != null)
            : [];
        BadgeStrip.SetBadges(_topPanel, badges, backed: true, scale: scale);
        _topPanel.Visible = badges.Count > 0;
    }

    private void SizeTopPanel(float scale)
    {
        var half = (BadgeRowHeight * scale + BackingInsets) / 2f;
        _topPanel!.OffsetTop = -half;
        _topPanel.OffsetBottom = half;
    }

    /// <summary>
    /// The gear menu's status line: whether the current quiet is correct. Counting
    /// what the tracker understands out of what the run actually holds.
    /// </summary>
    private string? StatusLine()
    {
        if (_player == null || !CombatManager.Instance.IsInProgress)
            return "Not bound to a combat";
        var trackedRelics = CountingTracker.TrackedRelics(_player).Count();
        var trackedPowers = CountingTracker.TrackedPowers(_player).Count();
        return $"Tracking {trackedRelics} of {_player.Relics.Count} relics, " +
            $"{trackedPowers} of {_player.Creature.Powers.Count} powers";
    }

    /// <summary>The native hand-count digit, absorbed as a "N. " strip prefix.</summary>
    private string? CountPrefix(NHandCardHolder holder)
    {
        if (!ViewOptions.ReplaceCardCount || !GodotObject.IsInstanceValid(holder))
            return null;
        var label = holder.GetNodeOrNull<Label>("%HandIndex");
        return label != null && label.Text.Length > 0 ? $"{label.Text}. " : null;
    }

    /// <summary>
    /// The game's SetIndexLabel just re-showed a digit this view absorbed; hide it
    /// again. Called from the patch, so it must stay cheap.
    /// </summary>
    public void ReassertIndexHidden(NHandCardHolder holder)
    {
        if (!ViewOptions.ReplaceCardCount)
            return;
        if (_hiddenIndexLabels.TryGetValue(holder, out var label) &&
            GodotObject.IsInstanceValid(label))
            label.Visible = false;
        else if (_handStrips.ContainsKey(holder))
            SyncIndexLabel(holder, hide: true);
    }

    private void SyncIndexLabel(NHandCardHolder holder, bool hide)
    {
        if (!GodotObject.IsInstanceValid(holder))
            return;
        if (hide)
        {
            if (_hiddenIndexLabels.ContainsKey(holder))
                return;
            var label = holder.GetNodeOrNull<Label>("%HandIndex");
            if (label == null)
                return;
            label.Visible = false;
            _hiddenIndexLabels[holder] = label;
        }
        else
        {
            RestoreIndexLabel(holder);
        }
    }

    private void RestoreIndexLabel(NHandCardHolder holder)
    {
        if (_hiddenIndexLabels.Remove(holder, out var label) &&
            GodotObject.IsInstanceValid(label))
            label.Visible = true;
    }

    private void RestoreIndexLabels()
    {
        foreach (var label in _hiddenIndexLabels.Values)
            if (GodotObject.IsInstanceValid(label))
                label.Visible = true;
        _hiddenIndexLabels.Clear();
    }

    /// <summary>
    /// The holder of the card play in progress, if any. It is reparented onto the
    /// hand node for the duration of the play and freed or returned afterwards.
    /// </summary>
    private static NHandCardHolder? InPlayHolder()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.InCardPlay)
            return null;
        // The play object is private, but it is a registered Godot script property.
        if (hand.Get("_currentCardPlay").As<GodotObject>() is NCardPlay play &&
            GodotObject.IsInstanceValid(play) &&
            GodotObject.IsInstanceValid(play.Holder))
            return play.Holder;
        // Fallback: the in-play holder is the one parented directly to the hand.
        foreach (var child in hand.GetChildren())
            if (child is NHandCardHolder holder)
                return holder;
        return null;
    }

    private static void FreeStrip(ref HBoxContainer? strip)
    {
        if (strip != null && GodotObject.IsInstanceValid(strip))
            strip.QueueFree();
        strip = null;
    }
}
