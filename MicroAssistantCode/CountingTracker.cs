using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>Hover-tip text for a badge; the title is the source's localized name.</summary>
internal sealed record BadgeTip(string Title, string Body);

/// <summary>
/// One icon in a strip. CountText is printed on the icon when present; Broken draws
/// an X instead. Primed colors the count gold: the very next tick triggers the
/// source. Projected colors it aqua: the card is held for play and the number is
/// what the counter is about to become, not what it is. WillTrigger marks a badge
/// whose source fires on (or applies its effect to) this very play — the trigger
/// banner is built from these. Tip is set only on End Turn badges; hand badges
/// must not catch the mouse. BlockValue marks a count that is an amount of Block,
/// which the card readout prints in the Block blue.
/// </summary>
internal sealed record SourceBadge(
    Texture2D Icon, string Id, string? CountText, bool Broken, bool Primed,
    bool Projected = false, bool WillTrigger = false, BadgeTip? Tip = null,
    bool BlockValue = false);

/// <summary>
/// Which counting sources — relics and powers — care about a card play or about
/// ending the turn.
///
/// The game exposes a general counter surface (ShowCounter, DisplayAmount, Status),
/// but "does playing this card advance or break the count" lives inside each
/// source's own hook overrides, so that part is taught here, one registry entry per
/// source. Counts come from the same public members the native UI prints, or from
/// CombatQueries mirroring a source's own history predicate — never from guessing,
/// so this display can not drift from the game.
/// </summary>
internal static class CountingTracker
{
    /// <summary>Mirrors PenNib's private const; it is not exposed as a DynamicVar.</summary>
    private const int PenNibThreshold = 10;

    /// <summary>Juggling fires on the hard-coded third attack of the turn.</summary>
    private const int JugglingThreshold = 3;

    private sealed record RelicSemantics(
        Func<RelicModel, CardModel, CardPlayEffect> PlayEffect,
        Func<RelicModel, EndTurnState> EndTurn,
        Func<RelicModel, string?> CountText,
        Func<RelicModel, bool> Primed,
        Func<RelicModel, EndTurnState, BadgeTip?> EndTurnTip,
        Func<RelicModel, CardModel, string?>? NextCount = null,
        bool Persistent = false,
        Func<RelicModel, int>? PendingBlock = null,
        bool CountIsBlock = false);

    private sealed record PowerSemantics(
        Func<PowerModel, Player, CardModel, CardPlayEffect> PlayEffect,
        Func<PowerModel, Player, EndTurnState> EndTurn,
        Func<PowerModel, Player, string?> CountText,
        Func<PowerModel, Player, bool> Primed,
        Func<PowerModel, Player, EndTurnState, BadgeTip?> EndTurnTip,
        Func<PowerModel, Player, string>? NextCount = null,
        bool TriggersWhileContributing = false,
        Func<PowerModel, Player, int>? PendingBlock = null,
        bool CountIsBlock = false);

    private static readonly Dictionary<Type, RelicSemantics> Relics = new()
    {
        // ---- Threshold conditions on this turn's card plays -------------------

        // Pocketwatch counts every card played this turn; DisplayAmount is that count.
        [typeof(Pocketwatch)] = new(
            PlayEffect: (relic, _) =>
                CountingRules.PocketwatchPlay(relic.DisplayAmount, IntVar(relic, "CardThreshold")),
            EndTurn: relic =>
                CountingRules.PocketwatchEndTurn(relic.DisplayAmount, IntVar(relic, "CardThreshold")),
            CountText: relic => relic.DisplayAmount.ToString(),
            Primed: _ => false,
            EndTurnTip: (relic, state) => new BadgeTip(
                Title(relic),
                state == EndTurnState.Active
                    ? $"Will draw {Cards(relic)} extra {CardWord(Cards(relic))} next turn."
                    : "Will NOT draw extra cards next turn."),
            NextCount: (relic, _) => CountingRules.NextUp(relic.DisplayAmount).ToString()),

        // Velvet Choker hard-blocks the card past its limit rather than costing a payoff.
        [typeof(VelvetChoker)] = new(
            PlayEffect: (relic, _) =>
                CountingRules.ChokerPlay(relic.DisplayAmount, Cards(relic)),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: relic => relic.DisplayAmount.ToString(),
            Primed: _ => false,
            EndTurnTip: (_, _) => null,
            NextCount: (relic, _) => CountingRules.NextUp(relic.DisplayAmount).ToString()),

        // Brilliant Scarf makes the Nth card of the turn free; quiet once spent.
        [typeof(BrilliantScarf)] = new(
            PlayEffect: (relic, _) =>
                CountingRules.ScarfPlay(relic.DisplayAmount, Cards(relic)),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: relic => relic.DisplayAmount.ToString(),
            Primed: relic => CountingRules.IsPrimed(relic.DisplayAmount, Cards(relic)),
            EndTurnTip: (_, _) => null,
            NextCount: (relic, _) => CountingRules.NextUp(relic.DisplayAmount).ToString()),

        // ---- "Don't do X this turn" payoffs -----------------------------------

        // Art of War: playing no attacks earns energy at the start of next turn.
        [typeof(ArtOfWar)] = new(
            PlayEffect: (relic, card) => CountingRules.AvoidancePlay(
                card.Type == CardType.Attack,
                payoffIntact: !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner)),
            EndTurn: relic => CountingRules.AvoidanceEndTurn(
                !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner)),
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (relic, state) => new BadgeTip(
                Title(relic),
                state == EndTurnState.Active
                    ? $"Will gain {IntVar(relic, "Energy")} energy next turn."
                    : "Will NOT gain energy next turn.")),

        // Ripple Basin: playing no attacks grants Block when the turn ends.
        [typeof(RippleBasin)] = new(
            PlayEffect: (relic, card) => CountingRules.AvoidancePlay(
                card.Type == CardType.Attack,
                payoffIntact: !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner)),
            EndTurn: relic => CountingRules.AvoidanceEndTurn(
                !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner)),
            CountText: relic => IntVar(relic, "Block").ToString(),
            Primed: _ => false,
            EndTurnTip: (relic, state) => new BadgeTip(
                Title(relic),
                state == EndTurnState.Active
                    ? $"Will gain {IntVar(relic, "Block")} Block when this turn ends."
                    : "Will NOT gain Block this turn."),
            PendingBlock: relic => CountingRules.PendingBlockGain(
                CountingRules.AvoidanceEndTurn(!CombatQueries.AnyAttackPlayedThisTurn(relic.Owner)),
                IntVar(relic, "Block")),
            CountIsBlock: true),

        // Pael's Tears: ending the turn with energy to spare earns energy at the
        // start of the next one. The card worth warning about is the one that would
        // spend the last of it — for an X-cost card that is any card, since
        // GetAmountToSpend reports the whole pool for those.
        [typeof(PaelsTears)] = new(
            PlayEffect: (relic, card) => CountingRules.TearsPlay(
                card.EnergyCost.GetAmountToSpend(), Energy(relic)),
            EndTurn: relic => CountingRules.TearsEndTurn(Energy(relic)),
            CountText: relic => IntVar(relic, "Energy").ToString(),
            Primed: _ => false,
            EndTurnTip: (relic, state) => new BadgeTip(
                Title(relic),
                state == EndTurnState.Active
                    ? $"Will gain {IntVar(relic, "Energy")} energy next turn."
                    : "Will NOT gain energy next turn.")),

        // Pael's Eye: a zero-card turn exhausts the hand and grants an extra turn,
        // once per combat. Its Status is Active exactly while that is still live.
        [typeof(PaelsEye)] = new(
            PlayEffect: (relic, _) =>
                CountingRules.PaelsEyePlay(relic.Status == RelicStatus.Active),
            EndTurn: relic =>
                CountingRules.PaelsEyeEndTurn(relic.Status == RelicStatus.Active),
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                "Will exhaust your hand and grant an extra turn if this turn ends now.")),

        // Rainbow Ring: an Attack, a Skill, and a Power in one turn. Its Status is
        // inverted relative to the others: Active means it already fired this turn.
        [typeof(RainbowRing)] = new(
            PlayEffect: (relic, card) => CountingRules.RainbowPlay(
                typeMissing: card.Type is CardType.Attack or CardType.Skill or CardType.Power &&
                    CombatQueries.PlaysOfTypeThisTurn(relic.Owner, card.Type) == 0,
                activatedThisTurn: relic.Status == RelicStatus.Active),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: relic => RainbowTypesFilled(relic.Owner).ToString(),
            Primed: relic => RainbowTypesFilled(relic.Owner) == 2,
            EndTurnTip: (_, _) => null,
            NextCount: (relic, _) =>
                CountingRules.NextUp(RainbowTypesFilled(relic.Owner)).ToString()),

        // ---- Every-Nth card tickers (count on the native bar; the badge adds
        //      "this card advances it") ----------------------------------------

        [typeof(Nunchaku)] = AttackTicker(persistent: true),
        [typeof(Kunai)] = AttackTicker(),
        [typeof(Shuriken)] = AttackTicker(),
        [typeof(OrnamentalFan)] = AttackTicker(),
        [typeof(Kusarigama)] = AttackTicker(),
        [typeof(LetterOpener)] = SkillTicker(),
        [typeof(TuningFork)] = SkillTicker(persistent: true),
        [typeof(IronClub)] = AnyCardTicker(persistent: true),

        // Joss Paper counts exhausted cards. Exhaust-keyword cards advance it on
        // play; Ethereal cards carry the badge as a reminder — they feed it only if
        // the turn ends with them in hand, so playing one neither advances the
        // count nor projects a next value.
        [typeof(JossPaper)] = new(
            Persistent: true,
            PlayEffect: (_, card) => CountingRules.TickerPlay(
                card.Keywords.Contains(CardKeyword.Exhaust) ||
                card.Keywords.Contains(CardKeyword.Ethereal)),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: relic => ((JossPaper)relic).CardsExhausted.ToString(),
            Primed: relic => CountingRules.IsPrimed(
                ((JossPaper)relic).CardsExhausted, IntVar(relic, "ExhaustAmount")),
            EndTurnTip: (_, _) => null,
            NextCount: (relic, card) => card.Keywords.Contains(CardKeyword.Exhaust)
                ? CountingRules.NextWrapped(
                    ((JossPaper)relic).CardsExhausted,
                    IntVar(relic, "ExhaustAmount")).ToString()
                : null),

        // Pen Nib's threshold is a const, so it cannot use the DynamicVar tickers.
        [typeof(PenNib)] = new(
            Persistent: true,
            PlayEffect: (_, card) =>
                CountingRules.PenNibPlay(card.Type == CardType.Attack),
            EndTurn: _ => CountingRules.PenNibEndTurn(),
            CountText: relic => relic.DisplayAmount.ToString(),
            Primed: relic => CountingRules.IsPrimed(relic.DisplayAmount, PenNibThreshold),
            EndTurnTip: (_, _) => null,
            NextCount: (relic, _) =>
                CountingRules.NextWrapped(relic.DisplayAmount, PenNibThreshold).ToString()),

        // ---- First-X triggers -------------------------------------------------

        // Music Box clones the first attack each turn; spent attacks read X so the
        // play order still matters today.
        [typeof(MusicBox)] = new(
            PlayEffect: (relic, card) => CountingRules.FirstMatchPlay(
                card.Type == CardType.Attack,
                armed: !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner),
                showSpentX: true),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),

        // Vambrace doubles the first block-gaining play of the combat; its Status
        // stays Active until the double is spent.
        [typeof(Vambrace)] = new(
            PlayEffect: (relic, card) => CountingRules.FirstMatchPlay(
                card.GainsBlock,
                armed: relic.Status == RelicStatus.Active,
                showSpentX: false),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),

        // Permafrost: the first Power card of the combat grants Block.
        [typeof(Permafrost)] = new(
            PlayEffect: (relic, card) => CountingRules.FirstMatchPlay(
                card.Type == CardType.Power,
                armed: !CombatQueries.AnyPowerPlayedThisCombat(relic.Owner),
                showSpentX: false),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),

        // Pael's Legion doubles a block card, then cools down; DisplayAmount is the
        // cooldown and drops to -1 once it is ready again.
        [typeof(PaelsLegion)] = new(
            PlayEffect: (relic, card) => CountingRules.FirstMatchPlay(
                card.GainsBlock,
                armed: relic.DisplayAmount <= 0,
                showSpentX: false),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),

        // ---- Turn tickers -----------------------------------------------------

        [typeof(Pendulum)] = new(
            Persistent: true,
            PlayEffect: (_, _) => CountingRules.PendulumPlay(),
            EndTurn: relic =>
                CountingRules.PendulumEndTurn(((Pendulum)relic).TurnsSeen, IntVar(relic, "Turns")),
            CountText: _ => null,
            Primed: relic =>
                CountingRules.IsPrimed(((Pendulum)relic).TurnsSeen, IntVar(relic, "Turns")),
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will draw {Cards(relic)} extra {CardWord(Cards(relic))} next turn.")),

        [typeof(HappyFlower)] = new(
            Persistent: true,
            PlayEffect: (_, _) => CardPlayEffect.None,
            EndTurn: relic =>
                CountingRules.PendulumEndTurn(((HappyFlower)relic).TurnsSeen, IntVar(relic, "Turns")),
            CountText: _ => null,
            Primed: relic =>
                CountingRules.IsPrimed(((HappyFlower)relic).TurnsSeen, IntVar(relic, "Turns")),
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will gain {IntVar(relic, "Energy")} energy next turn.")),

        [typeof(FakeHappyFlower)] = new(
            Persistent: true,
            PlayEffect: (_, _) => CardPlayEffect.None,
            EndTurn: relic => CountingRules.PendulumEndTurn(
                ((FakeHappyFlower)relic).TurnsSeen, IntVar(relic, "Turns")),
            CountText: _ => null,
            Primed: relic => CountingRules.IsPrimed(
                ((FakeHappyFlower)relic).TurnsSeen, IntVar(relic, "Turns")),
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will gain {IntVar(relic, "Energy")} energy next turn.")),

        // Pollinous Core counts turns without wrapping and resets after it fires.
        [typeof(PollinousCore)] = new(
            Persistent: true,
            PlayEffect: (_, _) => CardPlayEffect.None,
            EndTurn: relic => CountingRules.PollinousEndTurn(
                ((PollinousCore)relic).TurnsSeen, IntVar(relic, "Turns")),
            CountText: _ => null,
            Primed: relic => ((PollinousCore)relic).TurnsSeen >= IntVar(relic, "Turns") - 1,
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will draw {Cards(relic)} extra {CardWord(Cards(relic))} next turn.")),

        // Stone Calendar fires when its damage turn is ended; only that turn speaks.
        [typeof(StoneCalendar)] = new(
            PlayEffect: (_, _) => CardPlayEffect.None,
            EndTurn: relic => CountingRules.StoneCalendarEndTurn(
                relic.Owner.PlayerCombatState?.TurnNumber ?? 0, IntVar(relic, "DamageTurn")),
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will deal {IntVar(relic, "Damage")} damage to all enemies " +
                "when this turn ends.")),

        // ---- Block-or-not verdicts --------------------------------------------

        [typeof(Orichalcum)] = OrichalcumEntry(),
        [typeof(FakeOrichalcum)] = OrichalcumEntry(),

        // ---- Contextual reminders ---------------------------------------------

        // Cloak Clasp blocks per card in hand when the turn ends; its End Turn
        // badge carries the amount it is about to grant.
        [typeof(CloakClasp)] = new(
            PlayEffect: (_, _) => CardPlayEffect.None,
            EndTurn: _ => EndTurnState.Active,
            CountText: relic => CloakClaspBlock(relic).ToString(),
            Primed: _ => false,
            EndTurnTip: (relic, _) => new BadgeTip(
                Title(relic),
                $"Will gain {CloakClaspBlock(relic)} Block when this turn ends."),
            PendingBlock: CloakClaspBlock,
            CountIsBlock: true),

        // Paper Phrog deepens Vulnerable; attacks carry its icon as a reminder
        // while any enemy is currently Vulnerable.
        [typeof(PaperPhrog)] = new(
            PlayEffect: (relic, card) => CountingRules.TickerPlay(
                card.Type == CardType.Attack && AnyEnemyVulnerable(relic)),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),

        // Intimidating Helmet grants Block whenever a card is played with 2+
        // energy actually spent; cards currently costing that much carry its icon.
        [typeof(IntimidatingHelmet)] = new(
            PlayEffect: (relic, card) => CountingRules.TickerPlay(
                card.EnergyCost.GetAmountToSpend() >= relic.DynamicVars.Energy.IntValue),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null),
    };

    private static readonly Dictionary<Type, PowerSemantics> Powers = new()
    {
        // ---- Per-turn allowances whose remaining count the game never shows ----

        // Echo Form: the first N plays each turn happen twice.
        [typeof(EchoFormPower)] = Allowance(
            matches: (_, _) => true,
            used: (_, player) => CombatQueries.PlaySeriesStartedThisTurn(player)),

        // Void Form: the first N manual plays each turn are free.
        [typeof(VoidFormPower)] = Allowance(
            matches: (_, _) => true,
            used: (_, player) => CombatQueries.ManualSeriesFinishedThisTurn(player)),

        // Nostalgia: the first N Attacks/Skills go to the top of the draw pile.
        [typeof(NostalgiaPower)] = Allowance(
            matches: (_, card) => card.Type is CardType.Attack or CardType.Skill,
            used: (_, player) => CombatQueries.AttackOrSkillStartedThisTurn(player)),

        // Unmovable: the first N block-gaining plays give double Block.
        [typeof(UnmovablePower)] = Allowance(
            matches: (_, card) => card.GainsBlock,
            used: (_, player) => CombatQueries.BlockPlaysThisTurn(player)),

        // ---- Once-per-turn firsts ---------------------------------------------

        // Lethality: only the first attack each turn gets the bonus damage.
        [typeof(LethalityPower)] = FirstEachTurn(
            matches: (_, card) => card.Type == CardType.Attack,
            spent: (_, player) => CombatQueries.AttacksStartedThisTurn(player) > 0),

        // Phantom Blades: only the first Shiv each turn gets the bonus damage.
        [typeof(PhantomBladesPower)] = FirstEachTurn(
            matches: (_, card) => card.Tags.Contains(CardTag.Shiv),
            spent: (_, player) => CombatQueries.AnyShivPlayedThisTurn(player)),

        // ---- Nth-play triggers ------------------------------------------------

        // Juggling clones the third attack of the turn; later attacks read X.
        [typeof(JugglingPower)] = new(
            PlayEffect: (_, player, card) => CountingRules.AllowancePlay(
                card.Type == CardType.Attack,
                JugglingThreshold - CombatQueries.AttacksStartedThisTurn(player)),
            EndTurn: (_, _) => EndTurnState.Hidden,
            CountText: (_, player) =>
                Math.Min(CombatQueries.AttacksStartedThisTurn(player), JugglingThreshold).ToString(),
            Primed: (_, player) =>
                CombatQueries.AttacksStartedThisTurn(player) == JugglingThreshold - 1,
            EndTurnTip: (_, _, _) => null,
            NextCount: (_, player) => Math.Min(
                CountingRules.NextUp(CombatQueries.AttacksStartedThisTurn(player)),
                JugglingThreshold).ToString()),

        // Pale Blue Dot: five plays in a turn draws cards next turn. DisplayAmount
        // is the native countdown; once it hits zero the payoff is banked and the
        // End Turn badge carries the news instead of the hand badge.
        [typeof(PaleBlueDotPower)] = new(
            PlayEffect: (power, player, _) =>
                CombatQueries.PlaysFinishedThisTurn(player) >= PaleBlueDotPower.cardPlayThresholdValue
                    ? CardPlayEffect.None
                    : CardPlayEffect.Contributes,
            EndTurn: (power, player) =>
                CombatQueries.PlaysFinishedThisTurn(player) >= PaleBlueDotPower.cardPlayThresholdValue
                    ? EndTurnState.Active
                    : EndTurnState.Hidden,
            CountText: (power, _) => power.DisplayAmount.ToString(),
            Primed: (power, _) => power.DisplayAmount == 1,
            EndTurnTip: (power, _, _) => new BadgeTip(
                Title(power),
                $"Will draw {power.Amount} extra {CardWord(power.Amount)} next turn."),
            NextCount: (power, _) =>
                CountingRules.NextDown(power.DisplayAmount).ToString()),

        // Feel No Pain grants Block for every card exhausted, so the reminder has to
        // cover both halves: a card that exhausts itself on play, and a card whose
        // own effect exhausts others. The badge carries the Block per exhaust.
        [typeof(FeelNoPainPower)] = new(
            PlayEffect: (_, _, card) => CountingRules.TickerPlay(
                card.Keywords.Contains(CardKeyword.Exhaust) || ExhaustsOtherCards(card)),
            EndTurn: (_, _) => EndTurnState.Hidden,
            CountText: (power, _) => power.Amount.ToString(),
            Primed: (_, _) => false,
            EndTurnTip: (_, _, _) => null,
            // Every exhaust pays out, so a contributing card is always a trigger —
            // there is no Nth-play threshold to wait for.
            TriggersWhileContributing: true,
            CountIsBlock: true),

        // Plating grants its stacks as Block when the turn ends, then decays; the
        // End Turn badge is the reminder, carrying the amount about to be granted.
        [typeof(PlatingPower)] = new(
            PlayEffect: (_, _, _) => CardPlayEffect.None,
            EndTurn: (_, _) => EndTurnState.Active,
            CountText: (power, _) => power.Amount.ToString(),
            Primed: (_, _) => false,
            EndTurnTip: (power, _, _) => new BadgeTip(
                Title(power),
                $"Will gain {power.Amount} Block when this turn ends."),
            PendingBlock: (power, _) => power.Amount,
            CountIsBlock: true),

        // Panache: every fifth play hits all enemies; the counter is public via its
        // CardsLeft DynamicVar and counts down. It fires at zero and restarts from
        // five (mirrors the power's private _baseCardsLeft const).
        [typeof(PanachePower)] = new(
            PlayEffect: (_, _, _) => CardPlayEffect.Contributes,
            EndTurn: (_, _) => EndTurnState.Hidden,
            CountText: (power, _) => power.DynamicVars["CardsLeft"].IntValue.ToString(),
            Primed: (power, _) => power.DynamicVars["CardsLeft"].IntValue == 1,
            EndTurnTip: (_, _, _) => null,
            NextCount: (power, _) => CountingRules.NextCountdownWithReset(
                power.DynamicVars["CardsLeft"].IntValue, resetTo: 5).ToString()),
    };

    /// <summary>
    /// The badges to draw above a hovered card: relics, then powers. With
    /// <paramref name="projected" /> — the card is held for play — a contributing
    /// badge shows the value its counter is about to become instead of the current
    /// one, in the projection color.
    /// </summary>
    public static List<SourceBadge> ForCardPlay(
        Player player, CardModel card, bool projected = false)
    {
        var badges = new List<SourceBadge>();
        foreach (var (relic, semantics) in TrackedRelicPairs(player))
        {
            var effect = semantics.PlayEffect(relic, card);
            if (effect == CardPlayEffect.None)
                continue;
            var next = projected && effect == CardPlayEffect.Contributes
                ? semantics.NextCount?.Invoke(relic, card)
                : null;
            // A counter fires on this play when it is primed; an armed first-X
            // source (bare icon, no count) fires on any matching play.
            var willTrigger = effect == CardPlayEffect.Contributes &&
                (semantics.Primed(relic) || semantics.CountText(relic) == null);
            badges.Add(new SourceBadge(
                relic.Icon,
                relic.Id.Entry,
                next ?? semantics.CountText(relic),
                Broken: effect == CardPlayEffect.Breaks,
                Primed: next == null && semantics.Primed(relic),
                Projected: next != null,
                WillTrigger: willTrigger,
                BlockValue: semantics.CountIsBlock));
        }
        foreach (var (power, semantics) in TrackedPowerPairs(player))
        {
            var effect = semantics.PlayEffect(power, player, card);
            if (effect == CardPlayEffect.None)
                continue;
            var next = projected && effect == CardPlayEffect.Contributes
                ? semantics.NextCount?.Invoke(power, player)
                : null;
            var willTrigger = effect == CardPlayEffect.Contributes &&
                (semantics.Primed(power, player) ||
                 semantics.CountText(power, player) == null ||
                 semantics.TriggersWhileContributing);
            badges.Add(new SourceBadge(
                power.Icon,
                power.Id.Entry,
                next ?? semantics.CountText(power, player),
                Broken: effect == CardPlayEffect.Breaks,
                Primed: next == null && semantics.Primed(power, player),
                Projected: next != null,
                WillTrigger: willTrigger,
                BlockValue: semantics.CountIsBlock));
        }
        return badges;
    }

    /// <summary>The badges to draw above the End Turn button: relics, then powers.</summary>
    public static List<SourceBadge> ForEndTurn(Player player)
    {
        var badges = new List<SourceBadge>();
        foreach (var (relic, semantics) in TrackedRelicPairs(player))
        {
            var state = semantics.EndTurn(relic);
            if (state == EndTurnState.Hidden)
                continue;
            badges.Add(new SourceBadge(
                relic.Icon,
                relic.Id.Entry,
                semantics.CountText(relic),
                Broken: state == EndTurnState.Broken,
                semantics.Primed(relic),
                Tip: semantics.EndTurnTip(relic, state),
                BlockValue: semantics.CountIsBlock));
        }
        foreach (var (power, semantics) in TrackedPowerPairs(player))
        {
            var state = semantics.EndTurn(power, player);
            if (state == EndTurnState.Hidden)
                continue;
            badges.Add(new SourceBadge(
                power.Icon,
                power.Id.Entry,
                semantics.CountText(power, player),
                Broken: state == EndTurnState.Broken,
                semantics.Primed(power, player),
                Tip: semantics.EndTurnTip(power, player, state),
                BlockValue: semantics.CountIsBlock));
        }
        return badges;
    }

    /// <summary>
    /// The setup bar: the run-persistent relics, whose counters carry between
    /// combats, so their state is worth seeing before a card is ever hovered.
    /// </summary>
    public static List<SourceBadge> ForSetup(Player player)
    {
        var badges = new List<SourceBadge>();
        foreach (var (relic, semantics) in TrackedRelicPairs(player))
        {
            if (!semantics.Persistent)
                continue;
            badges.Add(new SourceBadge(
                relic.Icon,
                relic.Id.Entry,
                semantics.CountText(relic) ?? relic.DisplayAmount.ToString(),
                Broken: false,
                semantics.Primed(relic)));
        }
        return badges;
    }

    /// <summary>
    /// The Block that ending the turn right now would grant: every tracked source
    /// that is about to fire (Orichalcum, Fake Orichalcum, Ripple Basin, Cloak
    /// Clasp, Plating) plus each Frost orb's end-of-turn passive, as many times as
    /// it will trigger. Each gain goes through Hook.ModifyBlock — the same pass the
    /// game's card previews make — so No Block and future modifiers read right.
    /// </summary>
    public static int PendingEndTurnBlock(Player player)
    {
        var creature = player.Creature;
        if (creature.CombatState is not { } state)
            return 0;
        var gains = new List<decimal>();
        foreach (var (relic, semantics) in TrackedRelicPairs(player))
            if (semantics.PendingBlock?.Invoke(relic) is > 0 and var amount)
                gains.Add(amount);
        foreach (var (power, semantics) in TrackedPowerPairs(player))
            if (semantics.PendingBlock?.Invoke(power, player) is > 0 and var amount)
                gains.Add(amount);
        // Frost orbs: OrbModel.TriggerPassive's own count (Gold-Plated Cables adds
        // one for the front orb); the passive value already carries Focus.
        foreach (var orb in player.PlayerCombatState?.OrbQueue.Orbs ?? [])
        {
            if (orb is not FrostOrb)
                continue;
            var triggers = Hook.ModifyOrbPassiveTriggerCount(state, orb, 1, out _);
            for (var i = 0; i < triggers; i++)
                gains.Add(orb.PassiveVal);
        }
        return gains.Sum(gain => (int)Hook.ModifyBlock(
            state, creature, gain, ValueProp.Unpowered, null, null, out _));
    }

    /// <summary>The player's relics this mod understands, for event subscription.</summary>
    public static IEnumerable<RelicModel> TrackedRelics(Player player) =>
        TrackedRelicPairs(player).Select(pair => pair.Relic);

    /// <summary>The player's powers this mod understands, for the status line.</summary>
    public static IEnumerable<PowerModel> TrackedPowers(Player player) =>
        TrackedPowerPairs(player).Select(pair => pair.Power);

    private static IEnumerable<(RelicModel Relic, RelicSemantics Semantics)>
        TrackedRelicPairs(Player player)
    {
        // Melted relics stay in the list but no longer receive hooks; their counters
        // are frozen and the native bar stops counting them too.
        foreach (var relic in player.Relics)
            if (!relic.IsMelted && Relics.TryGetValue(relic.GetType(), out var semantics))
                yield return (relic, semantics);
    }

    private static IEnumerable<(PowerModel Power, PowerSemantics Semantics)>
        TrackedPowerPairs(Player player)
    {
        // Instanced powers (Panache) can appear more than once; each instance keeps
        // its own counter, so each gets its own badge.
        foreach (var power in player.Creature.Powers)
            if (Powers.TryGetValue(power.GetType(), out var semantics))
                yield return (power, semantics);
    }

    // ---- Entry builders -------------------------------------------------------

    private static RelicSemantics AttackTicker(bool persistent = false) =>
        Ticker(card => card.Type == CardType.Attack, persistent);

    private static RelicSemantics SkillTicker(bool persistent = false) =>
        Ticker(card => card.Type == CardType.Skill, persistent);

    private static RelicSemantics AnyCardTicker(bool persistent = false) =>
        Ticker(_ => true, persistent);

    /// <summary>
    /// Every-Nth relics whose DisplayAmount is the wrapped count and whose threshold
    /// is their Cards DynamicVar.
    /// </summary>
    private static RelicSemantics Ticker(
        Func<CardModel, bool> matches, bool persistent = false) => new(
        PlayEffect: (_, card) => CountingRules.TickerPlay(matches(card)),
        EndTurn: _ => EndTurnState.Hidden,
        CountText: relic => relic.DisplayAmount.ToString(),
        Primed: relic => CountingRules.IsPrimed(relic.DisplayAmount, Cards(relic)),
        EndTurnTip: (_, _) => null,
        NextCount: (relic, _) =>
            CountingRules.NextWrapped(relic.DisplayAmount, Cards(relic)).ToString(),
        Persistent: persistent);

    private static RelicSemantics OrichalcumEntry() => new(
        PlayEffect: (relic, card) => CountingRules.OrichalcumPlay(
            card.GainsBlock, payoffIntact: relic.Owner.Creature.Block == 0),
        EndTurn: relic => CountingRules.OrichalcumEndTurn(relic.Owner.Creature.Block),
        CountText: relic => IntVar(relic, "Block").ToString(),
        Primed: _ => false,
        EndTurnTip: (relic, state) => new BadgeTip(
            Title(relic),
            state == EndTurnState.Active
                ? $"Will gain {IntVar(relic, "Block")} Block when this turn ends."
                : "Will NOT gain Block this turn."),
        // Orichalcum reads the Block before anything else at turn end grants any
        // (its very-early hook), so only Block already up now cancels it.
        PendingBlock: relic => CountingRules.PendingBlockGain(
            CountingRules.OrichalcumEndTurn(relic.Owner.Creature.Block),
            IntVar(relic, "Block")),
        CountIsBlock: true);

    /// <summary>First-N-per-turn powers: count down the allowance, X when spent.</summary>
    private static PowerSemantics Allowance(
        Func<PowerModel, CardModel, bool> matches,
        Func<PowerModel, Player, int> used) => new(
        PlayEffect: (power, player, card) =>
            CountingRules.AllowancePlay(matches(power, card), power.Amount - used(power, player)),
        EndTurn: (_, _) => EndTurnState.Hidden,
        CountText: (power, player) =>
            Math.Max(0, power.Amount - used(power, player)).ToString(),
        Primed: (power, player) => power.Amount - used(power, player) == 1,
        EndTurnTip: (_, _, _) => null,
        NextCount: (power, player) =>
            CountingRules.NextDown(power.Amount - used(power, player)).ToString(),
        // An allowance applies its effect to every contributing play — this card
        // WILL be doubled / free / redirected — not only to the one that empties it.
        TriggersWhileContributing: true);

    /// <summary>Once-per-turn firsts: the bare icon while armed, X once spent.</summary>
    private static PowerSemantics FirstEachTurn(
        Func<PowerModel, CardModel, bool> matches,
        Func<PowerModel, Player, bool> spent) => new(
        PlayEffect: (power, player, card) => CountingRules.FirstMatchPlay(
            matches(power, card), armed: !spent(power, player), showSpentX: true),
        EndTurn: (_, _) => EndTurnState.Hidden,
        CountText: (_, _) => null,
        Primed: (_, _) => false,
        EndTurnTip: (_, _, _) => null);

    // ---- Model reads ----------------------------------------------------------

    private static int RainbowTypesFilled(Player player)
    {
        var filled = 0;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Attack) > 0) filled++;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Skill) > 0) filled++;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Power) > 0) filled++;
        return filled;
    }

    private static int CloakClaspBlock(RelicModel relic) =>
        (relic.Owner.PlayerCombatState?.Hand.Cards.Count ?? 0) * IntVar(relic, "Block");

    private static bool AnyEnemyVulnerable(RelicModel relic) =>
        relic.Owner.Creature.CombatState?.HittableEnemies
            .Any(enemy => enemy.HasPower<VulnerablePower>()) ?? false;

    /// <summary>
    /// Cards whose own effect exhausts other cards. There is no keyword or tag for
    /// this — <see cref="CardKeyword.Exhaust" /> only means "exhausts itself" — and
    /// the game decides it inside each card's effects, so the set is written out:
    /// the card models that call <c>CardCmd.Exhaust</c>, as of game 0.111.0. Types
    /// rather than ids on purpose, so a card that leaves the game breaks the build
    /// here instead of quietly dropping out of the reminder. A new card that
    /// exhausts others has to be added here by hand.
    /// </summary>
    private static readonly HashSet<Type> ExhaustingCards =
    [
        typeof(Brand), typeof(BurningPact), typeof(Cinder), typeof(Cleanse),
        typeof(FiendFire), typeof(FlakCannon), typeof(Purity), typeof(Scavenge),
        typeof(SecondWind), typeof(Stoke), typeof(Thrash), typeof(TrueGrit),
    ];

    private static bool ExhaustsOtherCards(CardModel card) =>
        ExhaustingCards.Contains(card.GetType());

    private static int Energy(RelicModel relic) =>
        relic.Owner.PlayerCombatState?.Energy ?? 0;

    private static int IntVar(RelicModel relic, string key) =>
        relic.DynamicVars[key].IntValue;

    private static int Cards(RelicModel relic) => relic.DynamicVars.Cards.IntValue;

    private static string CardWord(int count) => count == 1 ? "card" : "cards";

    /// <summary>
    /// The source's localized name. LocString.ToString is a debug representation
    /// ("LocString table relics entry ..."); GetFormattedText is the resolver.
    /// </summary>
    private static string Title(RelicModel relic) => relic.Title.GetFormattedText();

    private static string Title(PowerModel power) => power.Title.GetFormattedText();
}
