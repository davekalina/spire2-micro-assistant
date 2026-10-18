using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
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
        Func<RelicModel, PlayAhead?, int>? PendingBlock = null,
        bool CountIsBlock = false,
        Func<RelicModel, CardModel, BlockGain>? PlayBlock = null);

    private sealed record PowerSemantics(
        Func<PowerModel, Player, CardModel, CardPlayEffect> PlayEffect,
        Func<PowerModel, Player, EndTurnState> EndTurn,
        Func<PowerModel, Player, string?> CountText,
        Func<PowerModel, Player, bool> Primed,
        Func<PowerModel, Player, EndTurnState, BadgeTip?> EndTurnTip,
        Func<PowerModel, Player, string>? NextCount = null,
        bool TriggersWhileContributing = false,
        Func<PowerModel, Player, PlayAhead?, int>? PendingBlock = null,
        bool CountIsBlock = false,
        Func<PowerModel, Player, CardModel, BlockGain>? PlayBlock = null);

    /// <summary>
    /// Block a source grants because one card is played: <see cref="Times" /> gains
    /// of <see cref="Each" />, each passed through Hook.ModifyBlock the way the
    /// game passes each CreatureCmd.GainBlock.
    /// </summary>
    private readonly record struct BlockGain(decimal Each, int Times)
    {
        public bool Any => Each > 0 && Times > 0;
    }

    /// <summary>
    /// The card play the pending-Block shield is previewing. End-of-turn sources
    /// read the turn as if it had already happened: the card has left the hand,
    /// and any Block it brings is up before Orichalcum looks.
    /// </summary>
    private sealed record PlayAhead(CardModel Card, bool GainsBlock);

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
            PendingBlock: (relic, ahead) => CountingRules.PendingBlockGain(
                CountingRules.AvoidanceEndTurn(
                    !CombatQueries.AnyAttackPlayedThisTurn(relic.Owner) &&
                    ahead?.Card.Type != CardType.Attack),
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
        [typeof(OrnamentalFan)] = AttackTicker(paysBlock: true),
        [typeof(Kusarigama)] = AttackTicker(),
        [typeof(LetterOpener)] = SkillTicker(),
        [typeof(TuningFork)] = SkillTicker(persistent: true, paysBlock: true),
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
            EndTurnTip: (_, _) => null,
            PlayBlock: (relic, card) =>
                card.Type == CardType.Power && !CombatQueries.AnyPowerPlayedThisCombat(relic.Owner)
                    ? new BlockGain(IntVar(relic, "Block"), 1)
                    : default),

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
            PlayEffect: (relic, card) => CountingRules.TickerPlay(HelmetPays(relic, card)),
            EndTurn: _ => EndTurnState.Hidden,
            CountText: _ => null,
            Primed: _ => false,
            EndTurnTip: (_, _) => null,
            PlayBlock: (relic, card) => HelmetPays(relic, card)
                ? new BlockGain(IntVar(relic, "Block"), 1)
                : default),
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

        // Feel No Pain grants Block for every card exhausted, so the card badge has to
        // cover both halves — a card that exhausts itself on play, and a card whose
        // own effect exhausts others — and carries the Block this play brings. The
        // Ethereal cards still in hand exhaust when the turn ends; that Block rides
        // the End Turn badge and the pending shield.
        [typeof(FeelNoPainPower)] = BlockOnPlay(
            gain: (power, _, card) => new BlockGain(power.Amount, ExhaustsOnPlay(card)),
            endTurnBlock: (power, player, ahead) =>
                power.Amount * EtherealAtTurnEnd(player, ahead?.Card),
            endTurnTip: block => $"Will gain {block} Block as Ethereal cards exhaust " +
                "when this turn ends."),

        // ---- Block on play ----------------------------------------------------

        // Child of the Stars: Block per star a card spends.
        [typeof(ChildOfTheStarsPower)] = BlockOnPlay(
            (power, _, card) => new BlockGain(power.Amount * StarsSpent(card), 1)),

        // Pillar of Creation: Block per card generated for combat.
        [typeof(PillarOfCreationPower)] = BlockOnPlay(
            (power, player, card) => new BlockGain(power.Amount, CardsGenerated(player, card))),

        // Afterimage: Block for every card played.
        [typeof(AfterimagePower)] = BlockOnPlay(
            (power, _, _) => new BlockGain(power.Amount, 1)),

        // Rage: Block for every attack played this turn.
        [typeof(RagePower)] = BlockOnPlay(
            (power, _, card) => new BlockGain(power.Amount, card.Type == CardType.Attack ? 1 : 0)),

        // Danse Macabre: Block for a card costing at least its Energy DynamicVar.
        [typeof(DanseMacabrePower)] = BlockOnPlay(
            (power, _, card) => new BlockGain(power.Amount,
                card.EnergyCost.GetResolved() >= power.DynamicVars.Energy.IntValue ? 1 : 0)),

        // Spirit of Ash: Block for every Ethereal card played.
        [typeof(SpiritOfAshPower)] = BlockOnPlay(
            (power, _, card) => new BlockGain(power.Amount,
                card.Keywords.Contains(CardKeyword.Ethereal) ? 1 : 0)),

        // Shroud: Block every time a Doom application lands.
        [typeof(ShroudPower)] = BlockOnPlay(
            (power, _, card) => new BlockGain(power.Amount, DoomApplications(card))),

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
            PendingBlock: (power, _, _) => power.Amount,
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
            // A power that grants Block on play carries the Block this card brings.
            var playBlock = semantics.PlayBlock is { } gain
                ? ModifiedBlock(player, gain(power, player, card)).ToString()
                : null;
            badges.Add(new SourceBadge(
                power.Icon,
                power.Id.Entry,
                playBlock ?? next ?? semantics.CountText(power, player),
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
    /// The Block on its way: what ending the turn right now would grant — every
    /// tracked source about to fire (Orichalcum, Fake Orichalcum, Ripple Basin,
    /// Cloak Clasp, Plating, Feel No Pain on Ethereal cards) plus each Frost orb's
    /// end-of-turn passive, as many times as it will trigger.
    ///
    /// With <paramref name="card" /> — focused or held for play — it previews
    /// playing that card first: the Block the play itself sets off (Child of the
    /// Stars, Pillar of Creation, Feel No Pain, Afterimage, Rage, Danse Macabre,
    /// Spirit of Ash, Shroud, and the tracked relics that pay Block), then the
    /// turn end as it would stand afterwards. Each gain goes through
    /// Hook.ModifyBlock — the same pass the game's card previews make — so No
    /// Block and future modifiers read right.
    /// </summary>
    public static int PendingBlock(Player player, CardModel? card = null)
    {
        if (player.Creature.CombatState == null)
            return 0;
        var gains = new List<BlockGain>();
        if (card != null)
        {
            foreach (var (relic, semantics) in TrackedRelicPairs(player))
                if (semantics.PlayBlock?.Invoke(relic, card) is { Any: true } gain)
                    gains.Add(gain);
            foreach (var (power, semantics) in TrackedPowerPairs(player))
                if (semantics.PlayBlock?.Invoke(power, player, card) is { Any: true } gain)
                    gains.Add(gain);
        }
        var ahead = card != null ? new PlayAhead(card, card.GainsBlock || gains.Count > 0) : null;
        foreach (var (relic, semantics) in TrackedRelicPairs(player))
            if (semantics.PendingBlock?.Invoke(relic, ahead) is > 0 and var amount)
                gains.Add(new BlockGain(amount, 1));
        foreach (var (power, semantics) in TrackedPowerPairs(player))
            if (semantics.PendingBlock?.Invoke(power, player, ahead) is > 0 and var amount)
                gains.Add(new BlockGain(amount, 1));
        // Frost orbs: OrbModel.TriggerPassive's own count (Gold-Plated Cables adds
        // one for the front orb); the passive value already carries Focus.
        var state = player.Creature.CombatState;
        foreach (var orb in player.PlayerCombatState?.OrbQueue.Orbs ?? [])
            if (orb is FrostOrb)
                gains.Add(new BlockGain(
                    orb.PassiveVal, Hook.ModifyOrbPassiveTriggerCount(state, orb, 1, out _)));
        return gains.Sum(gain => ModifiedBlock(player, gain));
    }

    /// <summary>A gain's Block after Hook.ModifyBlock, once per CreatureCmd.GainBlock.</summary>
    private static int ModifiedBlock(Player player, BlockGain gain)
    {
        if (!gain.Any || player.Creature.CombatState is not { } state)
            return 0;
        return gain.Times * (int)Hook.ModifyBlock(
            state, player.Creature, gain.Each, ValueProp.Unpowered, null, null, out _);
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

    private static RelicSemantics AttackTicker(bool persistent = false, bool paysBlock = false) =>
        Ticker(card => card.Type == CardType.Attack, persistent, paysBlock);

    private static RelicSemantics SkillTicker(bool persistent = false, bool paysBlock = false) =>
        Ticker(card => card.Type == CardType.Skill, persistent, paysBlock);

    private static RelicSemantics AnyCardTicker(bool persistent = false) =>
        Ticker(_ => true, persistent);

    /// <summary>
    /// Every-Nth relics whose DisplayAmount is the wrapped count and whose threshold
    /// is their Cards DynamicVar. <paramref name="paysBlock" />: the payoff is the
    /// relic's Block DynamicVar (Ornamental Fan, Tuning Fork), granted on the play
    /// that fires it.
    /// </summary>
    private static RelicSemantics Ticker(
        Func<CardModel, bool> matches, bool persistent = false, bool paysBlock = false) => new(
        PlayEffect: (_, card) => CountingRules.TickerPlay(matches(card)),
        EndTurn: _ => EndTurnState.Hidden,
        CountText: relic => relic.DisplayAmount.ToString(),
        Primed: relic => CountingRules.IsPrimed(relic.DisplayAmount, Cards(relic)),
        EndTurnTip: (_, _) => null,
        NextCount: (relic, _) =>
            CountingRules.NextWrapped(relic.DisplayAmount, Cards(relic)).ToString(),
        Persistent: persistent,
        PlayBlock: paysBlock
            ? (relic, card) =>
                matches(card) && CountingRules.IsPrimed(relic.DisplayAmount, Cards(relic))
                    ? new BlockGain(IntVar(relic, "Block"), 1)
                    : default
            : null);

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
        // (its very-early hook), so only Block already up now — or brought by the
        // card being previewed — cancels it.
        PendingBlock: (relic, ahead) => CountingRules.PendingBlockGain(
            CountingRules.OrichalcumEndTurn(
                relic.Owner.Creature.Block, blockComing: ahead?.GainsBlock ?? false),
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

    /// <summary>
    /// Powers that grant Block as a card is played: the badge rides every card that
    /// would set one off, carrying the Block that play brings, and the pending
    /// shield adds it. <paramref name="endTurnBlock" /> also puts the power above
    /// End Turn whenever ending the turn would grant some.
    /// </summary>
    private static PowerSemantics BlockOnPlay(
        Func<PowerModel, Player, CardModel, BlockGain> gain,
        Func<PowerModel, Player, PlayAhead?, int>? endTurnBlock = null,
        Func<int, string>? endTurnTip = null) => new(
        PlayEffect: (power, player, card) =>
            CountingRules.TickerPlay(gain(power, player, card).Any),
        EndTurn: (power, player) => endTurnBlock?.Invoke(power, player, null) > 0
            ? EndTurnState.Active
            : EndTurnState.Hidden,
        CountText: (power, player) => endTurnBlock?.Invoke(power, player, null).ToString(),
        Primed: (_, _) => false,
        EndTurnTip: (power, player, _) => endTurnBlock != null && endTurnTip != null
            ? new BadgeTip(Title(power), endTurnTip(endTurnBlock(power, player, null)))
            : null,
        // Every matching play pays out; there is no threshold to wait for.
        TriggersWhileContributing: true,
        PendingBlock: endTurnBlock,
        CountIsBlock: true,
        PlayBlock: gain);

    // ---- Model reads ----------------------------------------------------------

    private static int RainbowTypesFilled(Player player)
    {
        var filled = 0;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Attack) > 0) filled++;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Skill) > 0) filled++;
        if (CombatQueries.PlaysOfTypeThisTurn(player, CardType.Power) > 0) filled++;
        return filled;
    }

    /// <summary>Cloak Clasp's Block for the hand left at turn end, less a card being played.</summary>
    private static int CloakClaspBlock(RelicModel relic, PlayAhead? ahead = null) =>
        CountingRules.HandAfterPlay(
            relic.Owner.PlayerCombatState?.Hand.Cards.Count ?? 0, playing: ahead != null) *
        IntVar(relic, "Block");

    /// <summary>Intimidating Helmet pays on 2+ energy actually spent (cardPlay.Resources).</summary>
    private static bool HelmetPays(RelicModel relic, CardModel card) =>
        card.EnergyCost.GetAmountToSpend() >= relic.DynamicVars.Energy.IntValue;

    /// <summary>
    /// The Ethereal cards that will exhaust when the turn ends, as CombatManager's
    /// DoTurnEnd picks them — minus a card about to be played out of the hand.
    /// </summary>
    private static int EtherealAtTurnEnd(Player player, CardModel? playing)
    {
        if (player.Creature.CombatState is not { } state)
            return 0;
        return player.PlayerCombatState?.Hand.Cards.Count(card =>
            card != playing &&
            card.Keywords.Contains(CardKeyword.Ethereal) &&
            (card.HasTurnEndInHandEffect || Hook.ShouldEtherealTrigger(state, card))) ?? 0;
    }

    /// <summary>Stars a card spends as it is played, as CardModel.SpendResources reads them.</summary>
    private static int StarsSpent(CardModel card) =>
        Math.Max(0, card.GetStarCostWithModifiers());

    private static bool AnyEnemyVulnerable(RelicModel relic) =>
        relic.Owner.Creature.CombatState?.HittableEnemies
            .Any(enemy => enemy.HasPower<VulnerablePower>()) ?? false;

    /// <summary>
    /// What a card's own effects do that no keyword or tag declares: how many other
    /// cards it exhausts (Feel No Pain), how many cards it generates for combat
    /// (Pillar of Creation), and how many Doom applications it makes (Shroud). The
    /// game decides each inside the card's effects, so the table is written out by
    /// hand, as of game 0.111.0 — re-derive it by grepping Models/Cards/ for
    /// <c>CardCmd.Exhaust</c>; for <c>AddGeneratedCard</c>, <c>CardCmd.Transform</c>,
    /// <c>Shiv.CreateInHand</c>, and <c>ForgeCmd.Forge</c>; and for
    /// <c>Apply&lt;DoomPower&gt;</c>. Types rather than ids on purpose, so a card that
    /// leaves the game breaks the build here instead of quietly dropping out. Each
    /// count is read off the card before it is played. A free choice ("up to N",
    /// Guards' any number) counts the most the play could do; a skippable
    /// choose-one counts the one.
    /// </summary>
    private sealed record CardEffects(
        Func<CardModel, int>? Exhausts = null,
        Func<CardModel, int>? Generates = null,
        Func<CardModel, int>? AppliesDoom = null);

    private static readonly Dictionary<Type, CardEffects> CardTable = new()
    {
        // ---- Exhausts other cards ----------------------------------------------
        [typeof(Brand)] = new(Exhausts: card => CountingRules.OneIfAny(OthersInHand(card).Count())),
        [typeof(BurningPact)] = new(Exhausts: card => CountingRules.OneIfAny(OthersInHand(card).Count())),
        [typeof(Cinder)] = new(Exhausts: card => CountingRules.OneIfAny(OthersInHand(card).Count())),
        [typeof(Cleanse)] = new(Exhausts: card =>
            CountingRules.OneIfAny(card.Owner.PlayerCombatState?.DrawPile.Cards.Count ?? 0)),
        [typeof(FiendFire)] = new(Exhausts: card => OthersInHand(card).Count()),
        [typeof(FlakCannon)] = new(Exhausts: card =>
            card.Owner.PlayerCombatState?.AllCards.Count(other =>
                other.Type == CardType.Status && other.Pile?.Type != PileType.Exhaust) ?? 0),
        [typeof(Purity)] = new(Exhausts: card =>
            Math.Min(card.DynamicVars.Cards.IntValue, OthersInHand(card).Count())),
        [typeof(Scavenge)] = new(Exhausts: card => CountingRules.OneIfAny(OthersInHand(card).Count())),
        [typeof(SecondWind)] = new(Exhausts: card =>
            OthersInHand(card).Count(other => other.Type != CardType.Attack)),
        // Stoke exhausts the hand, then generates as many cards as it exhausted.
        [typeof(Stoke)] = new(
            Exhausts: card => OthersInHand(card).Count(),
            Generates: card => OthersInHand(card).Count()),
        [typeof(Thrash)] = new(Exhausts: card =>
            CountingRules.OneIfAny(OthersInHand(card).Count(other => other.Type == CardType.Attack))),
        [typeof(TrueGrit)] = new(Exhausts: card => CountingRules.OneIfAny(OthersInHand(card).Count())),

        // ---- Generates cards for combat ----------------------------------------
        [typeof(Abundance)] = Generates(1),
        [typeof(AdaptiveStrike)] = Generates(1),
        [typeof(Anger)] = Generates(1),
        [typeof(Begone)] = new(Generates: card => CountingRules.OneIfAny(OthersInHand(card).Count())),
        [typeof(BladeDance)] = GeneratesCardsVar(),
        [typeof(BladeOfInk)] = GeneratesCardsVar(),
        [typeof(BladeSymphony)] = GeneratesCardsVar(),
        [typeof(BoostAway)] = Generates(1),
        [typeof(BundleOfJoy)] = GeneratesCardsVar(),
        [typeof(CaptureSpirit)] = GeneratesCardsVar(),
        [typeof(Charge)] = new(Generates: card => Math.Min(card.DynamicVars.Cards.IntValue,
            card.Owner.PlayerCombatState?.DrawPile.Cards.Count ?? 0)),
        [typeof(CloakAndDagger)] = GeneratesCardsVar(),
        [typeof(CollisionCourse)] = Generates(1),
        [typeof(Compact)] = new(Generates: card => OthersInHand(card).Count(other =>
            other.IsTransformable && other.Type == CardType.Status)),
        // Crash Landing fills the hand to its cap once this card has left it.
        [typeof(CrashLanding)] = new(Generates: card =>
            Math.Max(0, CardPile.MaxCardsInHand - OthersInHand(card).Count())),
        [typeof(Dirge)] = new(Generates: card => card.CombatState is { } state
            ? Hook.ModifyXValue(state, card, card.EnergyCost.GetAmountToSpend())
            : 0),
        [typeof(Discovery)] = Generates(1),
        [typeof(Distraction)] = Generates(1),
        [typeof(DualWield)] = new(Generates: card =>
            OthersInHand(card).Any(other => other.Type is CardType.Attack or CardType.Power)
                ? card.DynamicVars.Cards.IntValue
                : 0),
        [typeof(FanOfKnives)] = new(Generates: card => card.DynamicVars["Shivs"].IntValue),
        [typeof(FightThrough)] = Generates(2),
        [typeof(GlimpseBeyond)] = new(Generates: card =>
            card.DynamicVars.Cards.IntValue * LivingPlayersOnSide(card)),
        [typeof(GraveWarden)] = GeneratesCardsVar(),
        [typeof(Guards)] = new(Generates: card => OthersInHand(card).Count()),
        [typeof(GunkUp)] = Generates(1),
        [typeof(HeirloomHammer)] = new(Generates: card =>
            OthersInHand(card).Any(other => other.VisualCardPool.IsColorless)
                ? card.DynamicVars.Repeat.IntValue
                : 0),
        [typeof(HiddenDaggers)] = new(Generates: card => card.DynamicVars["Shivs"].IntValue),
        [typeof(InfernalBlade)] = Generates(1),
        [typeof(JackOfAllTrades)] = GeneratesCardsVar(),
        [typeof(Jackpot)] = GeneratesCardsVar(),
        [typeof(Largesse)] = Generates(1),
        [typeof(LeadingStrike)] = new(Generates: card => card.DynamicVars["Shivs"].IntValue),
        [typeof(MadScience)] = new(Generates: card =>
            ((MadScience)card).TinkerTimeRider == TinkerTime.RiderEffect.Chaos ? 1 : 0),
        [typeof(ManifestAuthority)] = Generates(1),
        [typeof(Metamorphosis)] = GeneratesCardsVar(),
        [typeof(Outrage)] = new(Generates: LivingPlayersOnSide),
        [typeof(Overclock)] = Generates(1),
        [typeof(PrimalForce)] = new(Generates: card => OthersInHand(card).Count(other =>
            other.IsTransformable && other.Type == CardType.Attack)),
        [typeof(Quasar)] = Generates(1),
        [typeof(Reave)] = GeneratesCardsVar(),
        [typeof(Seance)] = new(Generates: card => Math.Min(card.DynamicVars.Cards.IntValue,
            card.Owner.PlayerCombatState?.DrawPile.Cards.Count ?? 0)),
        [typeof(Severance)] = Generates(3),
        [typeof(Splash)] = Generates(1),
        [typeof(StormOfSteel)] = new(Generates: card => OthersInHand(card).Count()),
        [typeof(Turbo)] = Generates(1),
        [typeof(Undeath)] = Generates(1),
        [typeof(UpMySleeve)] = GeneratesCardsVar(),
        [typeof(WhiteNoise)] = Generates(1),
        // Forge makes a Sovereign Blade only when the player has none left.
        [typeof(BeatIntoShape)] = Forges(),
        [typeof(BigBang)] = Forges(),
        [typeof(Bulwark)] = Forges(),
        [typeof(Conqueror)] = Forges(),
        [typeof(RefineBlade)] = Forges(),
        [typeof(SeekingEdge)] = Forges(),
        [typeof(SpoilsOfBattle)] = Forges(),
        [typeof(SummonForth)] = Forges(),
        [typeof(TheSmith)] = Forges(),
        [typeof(WroughtInWar)] = Forges(),

        // ---- Applies Doom ------------------------------------------------------
        [typeof(BlightStrike)] = new(AppliesDoom: _ => 1),
        [typeof(NoEscape)] = new(AppliesDoom: _ => 1),
        [typeof(Scourge)] = new(AppliesDoom: _ => 1),
        [typeof(Deathbringer)] = new(AppliesDoom: HittableEnemies),
        [typeof(EndOfDays)] = new(AppliesDoom: HittableEnemies),
        [typeof(NegativePulse)] = new(AppliesDoom: HittableEnemies),
    };

    private static CardEffects Generates(int count) => new(Generates: _ => count);

    private static CardEffects GeneratesCardsVar() =>
        new(Generates: card => card.DynamicVars.Cards.IntValue);

    private static CardEffects Forges() => new(Generates: card =>
        card.Owner.PlayerCombatState?.AllCards.Any(other =>
            other is SovereignBlade && !other.IsDupe && other.Pile?.Type != PileType.Exhaust) ?? true
            ? 0
            : 1);

    private static IEnumerable<CardModel> OthersInHand(CardModel card) =>
        card.Owner.PlayerCombatState?.Hand.Cards.Where(other => other != card) ?? [];

    private static int LivingPlayersOnSide(CardModel card) =>
        card.CombatState?.GetTeammatesOf(card.Owner.Creature)
            .Count(creature => creature is { IsAlive: true, IsPlayer: true }) ?? 1;

    private static int HittableEnemies(CardModel card) =>
        card.CombatState?.HittableEnemies.Count ?? 0;

    /// <summary>Cards a play exhausts: itself under the Exhaust keyword, plus its own effect's.</summary>
    private static int ExhaustsOnPlay(CardModel card) =>
        (card.Keywords.Contains(CardKeyword.Exhaust) ? 1 : 0) +
        (CardTable.GetValueOrDefault(card.GetType())?.Exhausts?.Invoke(card) ?? 0);

    /// <summary>
    /// Cards generated for combat as this card is played, with the player as their
    /// creator: the card's own effect, plus the tracked clones it sets off — Music
    /// Box on the turn's first attack, Juggling on the third, Calamity on every one.
    /// </summary>
    private static int CardsGenerated(Player player, CardModel card)
    {
        var count = CardTable.GetValueOrDefault(card.GetType())?.Generates?.Invoke(card) ?? 0;
        if (card.Type != CardType.Attack)
            return count;
        var attacksBefore = CombatQueries.AttacksStartedThisTurn(player);
        if (player.Relics.Any(relic => relic is MusicBox { IsMelted: false }) &&
            !CombatQueries.AnyAttackPlayedThisTurn(player))
            count++;
        foreach (var power in player.Creature.Powers)
        {
            if (power is JugglingPower && attacksBefore == JugglingThreshold - 1)
                count += power.Amount;
            if (power is CalamityPower)
                count += power.Amount;
        }
        return count;
    }

    private static int DoomApplications(CardModel card) =>
        CardTable.GetValueOrDefault(card.GetType())?.AppliesDoom?.Invoke(card) ?? 0;

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
