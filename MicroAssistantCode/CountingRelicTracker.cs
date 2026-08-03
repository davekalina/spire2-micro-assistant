using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>Hover-tip text for a badge; the title is the relic's localized name.</summary>
internal sealed record BadgeTip(string Title, string Body);

/// <summary>
/// One icon in a strip: which relic, the count to print on it, and whether it is
/// drawn X-ed out instead. Primed marks a relic whose very next tick triggers it.
/// Tip is set only on End Turn badges; hand badges must not catch the mouse.
/// </summary>
internal sealed record RelicBadge(
    RelicModel Relic, int Count, bool Broken, bool Primed, BadgeTip? Tip = null);

/// <summary>
/// Which of a player's relics care about a card play or about ending the turn.
///
/// The game exposes a general counter surface (RelicModel.ShowCounter, DisplayAmount,
/// Status), but "does playing this card advance or break the count" lives inside each
/// relic's own hook overrides, so that part is taught here, one registry entry per
/// relic. Counts are read from the same members the native relic bar prints, never
/// recomputed, so this display can not drift from the bar.
/// </summary>
internal static class CountingRelicTracker
{
    /// <summary>Mirrors PenNib's private const; it is not exposed as a DynamicVar.</summary>
    private const int PenNibThreshold = 10;

    private sealed record Semantics(
        Func<RelicModel, CardModel, CardPlayEffect> PlayEffect,
        Func<RelicModel, EndTurnState> EndTurn,
        Func<RelicModel, int> Count,
        Func<RelicModel, bool> Primed,
        Func<RelicModel, EndTurnState, BadgeTip?> EndTurnTip);

    /// <summary>
    /// To teach the mod a new counting relic, add an entry. Decision logic belongs in
    /// CountingRules where the tests can reach it; entries only read the model.
    /// </summary>
    private static readonly Dictionary<Type, Semantics> Registry = new()
    {
        // Pocketwatch counts every card played this turn; DisplayAmount is that count.
        [typeof(Pocketwatch)] = new(
            PlayEffect: (relic, _) =>
                CountingRules.PocketwatchPlay(relic.DisplayAmount, CardThreshold(relic)),
            EndTurn: relic =>
                CountingRules.PocketwatchEndTurn(relic.DisplayAmount, CardThreshold(relic)),
            Count: relic => relic.DisplayAmount,
            Primed: _ => false,
            EndTurnTip: (relic, state) =>
            {
                var draw = relic.DynamicVars.Cards.IntValue;
                return new BadgeTip(
                    Title(relic),
                    state == EndTurnState.Active
                        ? $"Will draw {draw} extra {CardWord(draw)} next turn."
                        : "Will NOT draw extra cards next turn.");
            }),

        // Pendulum ticks at hand draw. TurnsSeen is read directly because DisplayAmount
        // reports the full cycle length during the relic's one-second activation flash.
        [typeof(Pendulum)] = new(
            PlayEffect: (_, _) => CountingRules.PendulumPlay(),
            EndTurn: relic =>
                CountingRules.PendulumEndTurn(((Pendulum)relic).TurnsSeen, Turns(relic)),
            Count: relic => ((Pendulum)relic).TurnsSeen,
            Primed: relic =>
                CountingRules.IsPrimed(((Pendulum)relic).TurnsSeen, Turns(relic)),
            EndTurnTip: (relic, _) =>
            {
                // Only ever shown while Active: the turn before the relic fires.
                var draw = relic.DynamicVars.Cards.IntValue;
                return new BadgeTip(
                    Title(relic),
                    $"Will draw {draw} extra {CardWord(draw)} next turn.");
            }),

        // Pen Nib counts attacks across turns; DisplayAmount is AttacksPlayed mod 10.
        [typeof(PenNib)] = new(
            PlayEffect: (_, card) =>
                CountingRules.PenNibPlay(card.Type == CardType.Attack),
            EndTurn: _ => CountingRules.PenNibEndTurn(),
            Count: relic => relic.DisplayAmount,
            Primed: relic => CountingRules.IsPrimed(relic.DisplayAmount, PenNibThreshold),
            EndTurnTip: (_, _) => null),
    };

    /// <summary>The badges to draw above a hovered card, in relic-bar order.</summary>
    public static List<RelicBadge> ForCardPlay(Player player, CardModel card)
    {
        var badges = new List<RelicBadge>();
        foreach (var (relic, semantics) in Tracked(player))
        {
            var effect = semantics.PlayEffect(relic, card);
            if (effect == CardPlayEffect.None)
                continue;
            badges.Add(new RelicBadge(
                relic,
                semantics.Count(relic),
                Broken: effect == CardPlayEffect.Breaks,
                semantics.Primed(relic)));
        }
        return badges;
    }

    /// <summary>The badges to draw above the End Turn button, in relic-bar order.</summary>
    public static List<RelicBadge> ForEndTurn(Player player)
    {
        var badges = new List<RelicBadge>();
        foreach (var (relic, semantics) in Tracked(player))
        {
            var state = semantics.EndTurn(relic);
            if (state == EndTurnState.Hidden)
                continue;
            badges.Add(new RelicBadge(
                relic,
                semantics.Count(relic),
                Broken: state == EndTurnState.Broken,
                semantics.Primed(relic),
                semantics.EndTurnTip(relic, state)));
        }
        return badges;
    }

    /// <summary>The player's relics this mod understands, for event subscription.</summary>
    public static IEnumerable<RelicModel> TrackedRelics(Player player) =>
        Tracked(player).Select(pair => pair.Relic);

    private static IEnumerable<(RelicModel Relic, Semantics Semantics)> Tracked(Player player)
    {
        // Melted relics stay in the list but no longer receive hooks; their counters
        // are frozen and the native bar stops counting them too.
        foreach (var relic in player.Relics)
            if (!relic.IsMelted && Registry.TryGetValue(relic.GetType(), out var semantics))
                yield return (relic, semantics);
    }

    private static int CardThreshold(RelicModel relic) =>
        relic.DynamicVars["CardThreshold"].IntValue;

    private static int Turns(RelicModel relic) => relic.DynamicVars["Turns"].IntValue;

    private static string CardWord(int count) => count == 1 ? "card" : "cards";

    /// <summary>
    /// The relic's localized name. LocString.ToString is a debug representation
    /// ("LocString table relics entry ..."); GetFormattedText is the resolver.
    /// </summary>
    private static string Title(RelicModel relic) => relic.Title.GetFormattedText();
}
