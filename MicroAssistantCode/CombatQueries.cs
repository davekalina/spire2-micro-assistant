using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.ValueProps;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// Reconstructions of per-turn counts whose owning relic or power keeps them in
/// private fields. Each query mirrors the exact predicate of the game class named on
/// it, over the public combat history — the same source several of those classes
/// query themselves. History entries only exist for live combats and are cleared per
/// combat, so no extra scoping is needed.
/// </summary>
internal static class CombatQueries
{
    private static CombatHistory History => CombatManager.Instance.History;

    private static ICombatState? StateOf(Player player) => player.Creature.CombatState;

    /// <summary>Ripple Basin's own predicate; also Art of War's and Music Box's tick.</summary>
    public static bool AnyAttackPlayedThisTurn(Player player) =>
        History.CardPlaysFinished.Any(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Card.Type == CardType.Attack &&
            e.CardPlay.Player == player);

    /// <summary>Pael's Eye's AnyCardsPlayedThisTurn: autoplays do not count.</summary>
    public static bool AnyManualPlayThisTurn(Player player) =>
        History.CardPlaysFinished.Any(e =>
            e.Actor == player.Creature &&
            e.HappenedThisTurn(StateOf(player)) &&
            !e.CardPlay.IsAutoPlay);

    /// <summary>Rainbow Ring's per-type tally (autoplays count, as in the relic).</summary>
    public static int PlaysOfTypeThisTurn(Player player, CardType type) =>
        History.CardPlaysFinished.Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Card.Type == type &&
            e.CardPlay.Player == player);

    /// <summary>Permafrost is once per combat; history is per combat, so no turn filter.</summary>
    public static bool AnyPowerPlayedThisCombat(Player player) =>
        History.CardPlaysFinished.Any(e =>
            e.CardPlay.Card.Type == CardType.Power &&
            e.CardPlay.Player == player);

    /// <summary>Echo Form's count: one per play series, autoplays included.</summary>
    public static int PlaySeriesStartedThisTurn(Player player) =>
        History.CardPlaysStarted.Count(e =>
            e.Actor == player.Creature &&
            e.CardPlay.IsFirstInSeries &&
            e.HappenedThisTurn(StateOf(player)));

    /// <summary>Void Form's count: manual plays only, one per finished series.</summary>
    public static int ManualSeriesFinishedThisTurn(Player player) =>
        History.CardPlaysFinished.Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Player == player &&
            !e.CardPlay.IsAutoPlay &&
            e.CardPlay.IsLastInSeries);

    /// <summary>Nostalgia's count of Attacks and Skills sent to the top of the draw pile.</summary>
    public static int AttackOrSkillStartedThisTurn(Player player) =>
        History.CardPlaysStarted.Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Card.Type is CardType.Attack or CardType.Skill &&
            e.CardPlay.Player == player);

    /// <summary>Unmovable's count of block-gaining card plays this turn.</summary>
    public static int BlockPlaysThisTurn(Player player) =>
        History.Entries.OfType<BlockGainedEntry>().Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay != null &&
            e.CardPlay.Player.Creature == player.Creature &&
            e.Props.IsCardOrMonsterMove());

    /// <summary>Lethality's and Juggling's count (both read plays as they start).</summary>
    public static int AttacksStartedThisTurn(Player player) =>
        History.CardPlaysStarted.Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Card.Type == CardType.Attack &&
            e.CardPlay.Player == player);

    /// <summary>Phantom Blades: has the once-per-turn Shiv bonus been spent?</summary>
    public static bool AnyShivPlayedThisTurn(Player player) =>
        History.CardPlaysFinished.Any(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Card.Tags.Contains(CardTag.Shiv) &&
            e.CardPlay.Player == player);

    /// <summary>Pale Blue Dot counts every finished play, autoplays included.</summary>
    public static int PlaysFinishedThisTurn(Player player) =>
        History.CardPlaysFinished.Count(e =>
            e.HappenedThisTurn(StateOf(player)) &&
            e.CardPlay.Player == player);
}
