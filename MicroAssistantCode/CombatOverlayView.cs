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
    // strip is the card's width, centered, overlapping the top of that label's
    // band - the digit itself draws lower and the first placement read as
    // floating too far off the card.
    private const float HandStripTop = -304f;
    private const float HandStripBottom = -264f;
    private const float HandStripHalfWidth = 150f;

    // The End Turn button root is 220x90 with the multiplayer vote row already at
    // y -15..15 and the long-press bar at y -11..-5; this band clears both. The x
    // range copies the vote row, whose center matches the button's.
    private const float EndTurnStripLeft = -16f;
    private const float EndTurnStripRight = 233f;
    private const float EndTurnStripTop = -64f;
    private const float EndTurnStripBottom = -24f;

    private readonly NCombatUi _ui;
    private readonly List<RelicModel> _subscribedRelics = [];
    private HBoxContainer? _endTurnStrip;
    private HBoxContainer? _handStrip;

    /// <summary>The holder the hand strip rides: the focused card, or the one in play.</summary>
    private NHandCardHolder? _stripHolder;

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
        Guard.Run("Following the focused hand card", SyncHandStrip);

    private void OnCombatStateChanged(CombatState _) =>
        Guard.Run("Refreshing badges on combat state change", () =>
        {
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
        BadgeStrip.SetBadges(_endTurnStrip, badges);
    }

    /// <summary>
    /// Move the hand strip to whichever holder deserves it now: the focused card in
    /// hand, or — so the information survives dragging the card out to play it —
    /// the holder of the card play in progress.
    /// </summary>
    private void SyncHandStrip()
    {
        var holder = CombatManager.Instance.IsInProgress
            ? NPlayerHand.Instance?.FocusedHolder ?? InPlayHolder()
            : null;
        if (holder == _stripHolder)
        {
            RefreshHandBadges();
            return;
        }

        FreeStrip(ref _handStrip);
        _stripHolder = holder;
        if (holder == null)
            return;

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
            return;
        var card = _stripHolder != null && GodotObject.IsInstanceValid(_stripHolder)
            ? _stripHolder.CardModel
            : null;
        var badges = card != null && CombatManager.Instance.IsInProgress
            ? CountingTracker.ForCardPlay(card.Owner, card)
            : [];
        BadgeStrip.SetBadges(_handStrip, badges);
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
