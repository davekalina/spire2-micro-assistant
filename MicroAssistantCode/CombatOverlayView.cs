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
    // strip is the card's width, centered, in the band just above that label.
    private const float HandStripTop = -336f;
    private const float HandStripBottom = -296f;
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
    private NHandCardHolder? _focusedHolder;
    private Player? _player;

    public CombatOverlayView(NCombatUi ui) => _ui = ui;

    public void Attach()
    {
        _endTurnStrip = RelicBadgeStrip.CreateContainer("MicroAssistantEndTurnStrip");
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
        UnbindPlayer();
        FreeStrip(ref _handStrip);
        FreeStrip(ref _endTurnStrip);
        _focusedHolder = null;
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
        foreach (var relic in CountingRelicTracker.TrackedRelics(_player))
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
            ? CountingRelicTracker.ForEndTurn(_player)
            : [];
        RelicBadgeStrip.SetBadges(_endTurnStrip, badges);
    }

    /// <summary>Move the hand strip to whichever holder is focused now, if any.</summary>
    private void SyncHandStrip()
    {
        var holder = CombatManager.Instance.IsInProgress
            ? NPlayerHand.Instance?.FocusedHolder
            : null;
        if (holder == _focusedHolder)
        {
            RefreshHandBadges();
            return;
        }

        FreeStrip(ref _handStrip);
        _focusedHolder = holder;
        if (holder == null)
            return;

        // Parented to the holder, the strip rides the card's pop-up motion and is
        // freed with it. The focused holder is unrotated at scale one, so these
        // holder-local offsets are exact screen shape.
        _handStrip = RelicBadgeStrip.CreateContainer("MicroAssistantHandStrip");
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
        var card = _focusedHolder != null && GodotObject.IsInstanceValid(_focusedHolder)
            ? _focusedHolder.CardModel
            : null;
        var badges = card != null && CombatManager.Instance.IsInProgress
            ? CountingRelicTracker.ForCardPlay(card.Owner, card)
            : [];
        RelicBadgeStrip.SetBadges(_handStrip, badges);
    }

    private static void FreeStrip(ref HBoxContainer? strip)
    {
        if (strip != null && GodotObject.IsInstanceValid(strip))
            strip.QueueFree();
        strip = null;
    }
}
