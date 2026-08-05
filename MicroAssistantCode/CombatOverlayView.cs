using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

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
    // - ReplaceCardCount: dropped onto the digit's own spot, which the strip
    //   absorbs as a "N." prefix while the digit itself is hidden.
    private const float HandStripTop = -348f;
    private const float HandStripBottom = -264f;
    private const float OverlayTop = -130f;
    private const float OverlayBottom = -46f;
    private const float ReplaceCountTop = -328f;
    private const float ReplaceCountBottom = -244f;
    private const float HandStripHalfWidth = 150f;

    // The End Turn button root is 220x90; this band hugs its top edge (the
    // multiplayer vote row at y -15..15 only exists in co-op, and drawing over it
    // was accepted in exchange for proximity). The extension is the ReplaceEndTurn
    // variant: flush on the button's top edge, full button width.
    private const float EndTurnStripLeft = -16f;
    private const float EndTurnStripRight = 233f;
    private const float EndTurnStripTop = -46f;
    private const float EndTurnStripBottom = -6f;
    private const float ExtensionTop = -60f;
    private const float ExtensionBottom = 0f;

    // While a card is held for play, the targeting arrow (GlobalUi, z of its source
    // control + 1) would draw over the strip. This absolute z outranks it, and is
    // applied only in that state so the strip can never punch through the overlay
    // screens during normal hover.
    private const int InPlayZ = 50;

    // The top-center panel sits at 20% down the screen ("80% of the way up").
    private const float TopPanelAnchorY = 0.2f;
    private const float TopPanelHalfHeight = 60f;

    private readonly NCombatUi _ui;
    private readonly List<RelicModel> _subscribedRelics = [];
    private readonly Dictionary<NHandCardHolder, HBoxContainer> _handStrips = [];
    private readonly Dictionary<NHandCardHolder, Label> _hiddenIndexLabels = [];
    private readonly OptionsPanel _options = new();
    private HBoxContainer? _endTurnStrip;
    private HBoxContainer? _endTurnExtension;
    private HBoxContainer? _topPanel;
    private Player? _player;

    public CombatOverlayView(NCombatUi ui) => _ui = ui;

    public void Attach()
    {
        _endTurnStrip = BadgeStrip.CreateContainer("MicroAssistantEndTurnStrip");
        // As a child of the button it inherits the show/hide tweens, including the
        // slide out on enemy turns. The hover animation moves only the button's
        // Visuals child, so the strip does not jitter with it.
        _ui.EndTurnButton.AddChild(_endTurnStrip);

        _endTurnExtension = BadgeStrip.CreateContainer("MicroAssistantEndTurnExtension");
        _endTurnExtension.OffsetLeft = 0f;
        _endTurnExtension.OffsetRight = 220f;
        _endTurnExtension.OffsetTop = ExtensionTop;
        _endTurnExtension.OffsetBottom = ExtensionBottom;
        _ui.EndTurnButton.AddChild(_endTurnExtension);

        // Full-width so its centered content needs no measuring; only visible while
        // a card is focused or in play, which is also why its absolute z (over the
        // targeting arrow) is safe to leave on permanently.
        _topPanel = BadgeStrip.CreateTriggerBanner("MicroAssistantTopPanel");
        _topPanel.AnchorLeft = 0f;
        _topPanel.AnchorRight = 1f;
        _topPanel.AnchorTop = TopPanelAnchorY;
        _topPanel.AnchorBottom = TopPanelAnchorY;
        _topPanel.OffsetTop = -TopPanelHalfHeight;
        _topPanel.OffsetBottom = TopPanelHalfHeight;
        _topPanel.ZAsRelative = false;
        _topPanel.ZIndex = InPlayZ;
        _ui.AddChild(_topPanel);

        Guard.Run("Attaching the view options panel", () =>
        {
            if (NRun.Instance != null)
                _options.Attach(NRun.Instance.GlobalUi);
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
        if (_endTurnExtension != null && GodotObject.IsInstanceValid(_endTurnExtension))
            BadgeStrip.SetBadges(
                _endTurnExtension, ViewOptions.ReplaceEndTurn ? badges : [], backed: true);
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
            // Big & Bold moves the active card's display to the top panel; without
            // Always On its above-card strip disappears entirely.
            if (ViewOptions.BigAndBold && !ViewOptions.AlwaysOn && active != null)
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

        var (top, bottom) = ViewOptions.CardOverlay ? (OverlayTop, OverlayBottom)
            : ViewOptions.ReplaceCardCount ? (ReplaceCountTop, ReplaceCountBottom)
            : (HandStripTop, HandStripBottom);
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
            strip, badges, backed: !ViewOptions.CardOverlay, prefix: CountPrefix(holder));
        SyncIndexLabel(holder, hide: ViewOptions.ReplaceCardCount && badges.Count > 0);
    }

    /// <summary>
    /// The top-center panel: under Big &amp; Bold it carries the active card's full
    /// badge row at double size whenever a card is focused or in play; otherwise it
    /// shows the icons of what a play in progress will set off.
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

        if (!ViewOptions.BigAndBold)
        {
            var triggering = card != null && inPlay != null
                ? CountingTracker.ForCardPlay(card.Owner, card, projected: true)
                    .Where(badge => badge.WillTrigger).ToList()
                : [];
            BadgeStrip.SetTriggerIcons(_topPanel, triggering);
            return;
        }

        var badges = card != null
            ? CountingTracker.ForCardPlay(card.Owner, card, projected: inPlay != null)
            : [];
        BadgeStrip.SetBadges(_topPanel, badges, backed: true, scale: 2f,
            prefix: active != null && ViewOptions.ReplaceCardCount ? CountPrefix(active) : null);
        _topPanel.Visible = badges.Count > 0;
    }

    /// <summary>The native hand-count digit, absorbed as a "N." strip prefix.</summary>
    private string? CountPrefix(NHandCardHolder holder)
    {
        if (!ViewOptions.ReplaceCardCount || !GodotObject.IsInstanceValid(holder))
            return null;
        var label = holder.GetNodeOrNull<Label>("%HandIndex");
        return label != null && label.Text.Length > 0 ? $"{label.Text}." : null;
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
