using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The two badge strips of one combat: above the focused hand card, and above the
/// End Turn button. Created when the combat UI readies, disposed when it leaves.
///
/// Everything is event-driven off the same signals the native UI repaints on —
/// relic DisplayAmountChanged/StatusChanged for counts, HoveredModelTracker for the
/// focused card (which both mouse hover and controller focus feed), CombatManager
/// for lifecycle. No polling, no per-frame work.
/// </summary>
internal sealed class CombatOverlayView : IDisposable
{
    // The hand card holder's origin is the card's center and the card spans
    // y -211..211; the native %HandIndex hotkey label occupies y -288..-224. The
    // strip's bottom edge stays where the bare icons sat; the tooltip backing
    // grows the band upward from there so the hotkey digit stays uncovered.
    private const float HandStripTop = -348f;
    private const float HandStripBottom = -264f;
    private const float HandStripHalfWidth = 150f;

    // The End Turn button root is 220x90 with the multiplayer vote row already at
    // y -15..15 and the long-press bar at y -11..-5; this band clears both. The x
    // range copies the vote row, whose center matches the button's.
    private const float EndTurnStripLeft = -16f;
    private const float EndTurnStripRight = 233f;
    private const float EndTurnStripTop = -64f;
    private const float EndTurnStripBottom = -24f;

    // While a card is held for play, the targeting arrow (GlobalUi, z of its source
    // control + 1) would draw over the strip. This absolute z outranks it, and is
    // applied only in that state so the strip can never punch through the overlay
    // screens during normal hover.
    private const int InPlayZ = 50;

    // The trigger banner sits centered at 20% down the screen ("80% of the way up").
    private const float BannerAnchorY = 0.2f;
    private const float BannerHalfHeight = 42f;

    private readonly NCombatUi _ui;
    private readonly List<RelicModel> _subscribedRelics = [];
    private HBoxContainer? _endTurnStrip;
    private HBoxContainer? _handStrip;
    private HBoxContainer? _triggerBanner;

    /// <summary>The holder the hand strip rides: the focused card, or the one in play.</summary>
    private NHandCardHolder? _stripHolder;

    /// <summary>True while the strip rides a card held for play: counts are projected.</summary>
    private bool _projected;

    private Player? _player;

    public CombatOverlayView(NCombatUi ui) => _ui = ui;

    public void Attach()
    {
        _endTurnStrip = BadgeStrip.CreateContainer("MicroAssistantEndTurnStrip");
        _endTurnStrip.OffsetLeft = EndTurnStripLeft;
        _endTurnStrip.OffsetRight = EndTurnStripRight;
        _endTurnStrip.OffsetTop = EndTurnStripTop;
        _endTurnStrip.OffsetBottom = EndTurnStripBottom;
        // As a child of the button it inherits the show/hide tweens, including the
        // slide out on enemy turns. The hover animation moves only the button's
        // Visuals child, so the strip does not jitter with it.
        _ui.EndTurnButton.AddChild(_endTurnStrip);

        // Full-width so its centered content needs no measuring; only visible while
        // a play in progress will set something off, which is also why its absolute
        // z (over the targeting arrow) is safe to leave on permanently.
        _triggerBanner = BadgeStrip.CreateTriggerBanner("MicroAssistantTriggerBanner");
        _triggerBanner.AnchorLeft = 0f;
        _triggerBanner.AnchorRight = 1f;
        _triggerBanner.AnchorTop = BannerAnchorY;
        _triggerBanner.AnchorBottom = BannerAnchorY;
        _triggerBanner.OffsetTop = -BannerHalfHeight;
        _triggerBanner.OffsetBottom = BannerHalfHeight;
        _triggerBanner.ZAsRelative = false;
        _triggerBanner.ZIndex = InPlayZ;
        _ui.AddChild(_triggerBanner);

        CombatManager.Instance.CombatBegan += OnCombatBegan;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        CombatManager.Instance.TurnStarted += OnTurnChanged;
        CombatManager.Instance.TurnEnded += OnTurnChanged;
        RunManager.Instance.HoveredModelTracker.HoverChanged += OnHoverChanged;
        // Powers have no per-instance events worth wiring; the tracker's coalesced
        // once-per-frame signal covers power changes, history ticks, and anything
        // else the specific subscriptions above miss.
        CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;

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
        UnbindPlayer();
        FreeStrip(ref _handStrip);
        FreeStrip(ref _endTurnStrip);
        FreeStrip(ref _triggerBanner);
        _stripHolder = null;
    }

    private void OnCombatBegan(CombatState state) =>
        Guard.Run("Binding the overlay to the new combat", () => BindPlayer(state));

    private void OnCombatEnded(CombatRoom _) =>
        Guard.Run("Unbinding the overlay from the ended combat", () =>
        {
            UnbindPlayer();
            RefreshEndTurn();
            SyncHandStrip();
        });

    private void OnTurnChanged(CombatState _) =>
        Guard.Run("Refreshing the End Turn badges on turn change", RefreshEndTurn);

    private void OnHoverChanged(ulong _) =>
        Guard.Run("Following the focused hand card", () =>
        {
            Diag.Once("hover", () => "Hover event received.");
            SyncHandStrip();
        });

    private void OnCombatStateChanged(CombatState _) =>
        Guard.Run("Refreshing badges on combat state change", () =>
        {
            Diag.Once("statechange", () => "Combat state change received.");
            RefreshEndTurn();
            SyncHandStrip();
        });

    /// <summary>A tracked relic's counter or status moved; both strips may care.</summary>
    private void OnRelicChanged() =>
        Guard.Run("Refreshing badges on a relic change", () =>
        {
            RefreshEndTurn();
            RefreshHandBadges();
        });

    private void OnRelicListChanged(RelicModel _) =>
        Guard.Run("Rewiring badges after a relic was gained or lost", () =>
        {
            ResubscribeRelics();
            RefreshEndTurn();
            RefreshHandBadges();
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
        if (_endTurnStrip == null || !GodotObject.IsInstanceValid(_endTurnStrip))
            return;
        var badges = _player != null && CombatManager.Instance.IsInProgress
            ? CountingTracker.ForEndTurn(_player)
            : [];
        Diag.Once("endturn", () =>
            $"End Turn strip refreshed: {badges.Count} badge(s), " +
            $"player {(_player == null ? "unbound" : "bound")}.");
        BadgeStrip.SetBadges(_endTurnStrip, badges);
    }

    /// <summary>Resync now; for patches that fire on transitions no event covers.</summary>
    public void SyncHandStripNow() => SyncHandStrip();

    /// <summary>
    /// Move the hand strip to whichever holder deserves it now: the card play in
    /// progress — so the information survives dragging the card out, and switches
    /// to projected counts — or the focused card in hand. In-play wins because at
    /// the moment a play starts the old holder may still briefly hold focus.
    /// </summary>
    private void SyncHandStrip()
    {
        NHandCardHolder? holder = null;
        _projected = false;
        if (CombatManager.Instance.IsInProgress)
        {
            var inPlay = InPlayHolder();
            _projected = inPlay != null;
            holder = inPlay ?? NPlayerHand.Instance?.FocusedHolder;
        }
        if (holder == _stripHolder)
        {
            // Same holder, but hover may have just become a play (or the reverse);
            // the refresh re-reads _projected either way.
            RefreshHandBadges();
            return;
        }

        FreeStrip(ref _handStrip);
        _stripHolder = holder;
        if (holder == null)
        {
            UpdateTriggerBanner([]);
            return;
        }

        // Parented to the holder, the strip rides the card's pop-up motion and is
        // freed with it. The focused holder is unrotated at scale one, so these
        // holder-local offsets are exact screen shape.
        _handStrip = BadgeStrip.CreateContainer("MicroAssistantHandStrip");
        _handStrip.OffsetLeft = -HandStripHalfWidth;
        _handStrip.OffsetRight = HandStripHalfWidth;
        _handStrip.OffsetTop = HandStripTop;
        _handStrip.OffsetBottom = HandStripBottom;
        holder.AddChild(_handStrip);
        RefreshHandBadges();
    }

    private void RefreshHandBadges()
    {
        if (_handStrip == null || !GodotObject.IsInstanceValid(_handStrip))
        {
            UpdateTriggerBanner([]);
            return;
        }
        var card = _stripHolder != null && GodotObject.IsInstanceValid(_stripHolder)
            ? _stripHolder.CardModel
            : null;
        var badges = card != null && CombatManager.Instance.IsInProgress
            ? CountingTracker.ForCardPlay(card.Owner, card, _projected)
            : [];
        Diag.Once("hand", () =>
            $"Hand strip refreshed: {badges.Count} badge(s) for " +
            $"{card?.Id.Entry ?? "no card"}.");
        // Above the targeting arrow only while a card is in play; relative z while
        // merely hovering, so overlays keep drawing over the strip.
        _handStrip.ZAsRelative = !_projected;
        _handStrip.ZIndex = _projected ? InPlayZ : 0;
        BadgeStrip.SetBadges(_handStrip, badges, backed: true);
        UpdateTriggerBanner(badges);
    }

    /// <summary>The banner speaks only while a play in progress will set something off.</summary>
    private void UpdateTriggerBanner(IReadOnlyList<SourceBadge> badges)
    {
        if (_triggerBanner == null || !GodotObject.IsInstanceValid(_triggerBanner))
            return;
        var triggering = _projected
            ? badges.Where(badge => badge.WillTrigger).ToList()
            : [];
        BadgeStrip.SetTriggerIcons(_triggerBanner, triggering);
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
