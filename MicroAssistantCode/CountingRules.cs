namespace MicroAssistant.MicroAssistantCode;

/// <summary>What playing a specific card right now would do to a counting relic.</summary>
public enum CardPlayEffect
{
    /// <summary>The relic does not care about this card.</summary>
    None,

    /// <summary>The play counts toward the relic; show its icon with the current count.</summary>
    Contributes,

    /// <summary>The play would push the relic past its condition; show its icon X-ed out.</summary>
    Breaks,
}

/// <summary>Whether a counting relic belongs above the End Turn button right now.</summary>
public enum EndTurnState
{
    /// <summary>Not turn-scoped, or nothing to say this turn.</summary>
    Hidden,

    /// <summary>Ending the turn now keeps (or brings) the relic's payoff.</summary>
    Active,

    /// <summary>The relic's condition is already lost this turn; show it X-ed out.</summary>
    Broken,
}

/// <summary>
/// The decision logic, on plain numbers so the test project can link this file without
/// loading the game. Counts and thresholds come from the live relic models; see
/// CountingRelicTracker for where each number is read.
/// </summary>
public static class CountingRules
{
    /// <summary>
    /// Pocketwatch draws extra next turn if this turn ends with at most
    /// <paramref name="cardThreshold" /> cards played. Every card play counts, so the
    /// question for a hovered card is whether it is the play that goes over.
    /// </summary>
    public static CardPlayEffect PocketwatchPlay(int cardsPlayedThisTurn, int cardThreshold) =>
        cardsPlayedThisTurn >= cardThreshold ? CardPlayEffect.Breaks : CardPlayEffect.Contributes;

    public static EndTurnState PocketwatchEndTurn(int cardsPlayedThisTurn, int cardThreshold) =>
        cardsPlayedThisTurn <= cardThreshold ? EndTurnState.Active : EndTurnState.Broken;

    /// <summary>Pendulum ticks on turns, not card plays; no card ever shows it.</summary>
    public static CardPlayEffect PendulumPlay() => CardPlayEffect.None;

    /// <summary>
    /// Pendulum fires at the start of the turn after its counter reaches
    /// <paramref name="turns" /> − 1, so that is the one turn it earns a spot above the
    /// End Turn button. It cannot be broken, only waited for.
    /// </summary>
    public static EndTurnState PendulumEndTurn(int turnsSeen, int turns) =>
        turnsSeen == turns - 1 ? EndTurnState.Active : EndTurnState.Hidden;

    /// <summary>Pen Nib counts attacks, wherever in the turn they land.</summary>
    public static CardPlayEffect PenNibPlay(bool isAttack) =>
        isAttack ? CardPlayEffect.Contributes : CardPlayEffect.None;

    /// <summary>Pen Nib is turn-agnostic: ending the turn neither helps nor hurts it.</summary>
    public static EndTurnState PenNibEndTurn() => EndTurnState.Hidden;

    /// <summary>The next contributing play is the one that triggers the relic.</summary>
    public static bool IsPrimed(int count, int threshold) => count == threshold - 1;

    /// <summary>
    /// Every-Nth tickers (Nunchaku, Kunai, Iron Club, ...): a matching card advances
    /// the count and nothing can break it.
    /// </summary>
    public static CardPlayEffect TickerPlay(bool cardMatches) =>
        cardMatches ? CardPlayEffect.Contributes : CardPlayEffect.None;

    /// <summary>
    /// Brilliant Scarf: the Nth card each turn is free; once the window is spent for
    /// the turn there is nothing left to say.
    /// </summary>
    public static CardPlayEffect ScarfPlay(int playedThisTurn, int threshold) =>
        playedThisTurn < threshold ? CardPlayEffect.Contributes : CardPlayEffect.None;

    /// <summary>Velvet Choker: a hard cap; the card at the limit is refused outright.</summary>
    public static CardPlayEffect ChokerPlay(int playedThisTurn, int limit) =>
        playedThisTurn >= limit ? CardPlayEffect.Breaks : CardPlayEffect.Contributes;

    /// <summary>
    /// "Don't do X this turn" (Art of War, Ripple Basin): a matching play forfeits
    /// the payoff, and keeps reading as forfeited for the rest of the turn.
    /// </summary>
    public static CardPlayEffect AvoidancePlay(bool cardMatches) =>
        cardMatches ? CardPlayEffect.Breaks : CardPlayEffect.None;

    public static EndTurnState AvoidanceEndTurn(bool conditionIntact) =>
        conditionIntact ? EndTurnState.Active : EndTurnState.Broken;

    /// <summary>
    /// Pael's Eye: any play breaks it, but it is once per combat and retryable next
    /// turn, so it only speaks while the zero-card turn is still achievable.
    /// </summary>
    public static CardPlayEffect PaelsEyePlay(bool intactAndUnused) =>
        intactAndUnused ? CardPlayEffect.Breaks : CardPlayEffect.None;

    public static EndTurnState PaelsEyeEndTurn(bool intactAndUnused) =>
        intactAndUnused ? EndTurnState.Active : EndTurnState.Hidden;

    /// <summary>
    /// Rainbow Ring: a card contributes only while the trio is unfinished and this
    /// card's type is still missing this turn.
    /// </summary>
    public static CardPlayEffect RainbowPlay(bool typeMissing, bool activatedThisTurn) =>
        !activatedThisTurn && typeMissing ? CardPlayEffect.Contributes : CardPlayEffect.None;

    /// <summary>
    /// Pollinous Core counts turns without wrapping and fires at the hand draw once
    /// past the cycle, so "primed" is at-or-beyond rather than Pendulum's exact hit.
    /// </summary>
    public static EndTurnState PollinousEndTurn(int turnsSeen, int turns) =>
        turnsSeen >= turns - 1 ? EndTurnState.Active : EndTurnState.Hidden;

    /// <summary>Stone Calendar: fires when its damage turn is ended.</summary>
    public static EndTurnState StoneCalendarEndTurn(int turnNumber, int damageTurn) =>
        turnNumber == damageTurn ? EndTurnState.Active : EndTurnState.Hidden;

    /// <summary>
    /// First-match triggers (Music Box each turn; Vambrace, Permafrost, Pael's
    /// Legion per combat or cooldown): the icon while armed. Per-turn sources show a
    /// spent X — the order of plays still matters today; per-combat sources go
    /// quiet instead of X-ing every qualifying card for the rest of the fight.
    /// </summary>
    public static CardPlayEffect FirstMatchPlay(bool cardMatches, bool armed, bool showSpentX) =>
        !cardMatches ? CardPlayEffect.None :
        armed ? CardPlayEffect.Contributes :
        showSpentX ? CardPlayEffect.Breaks : CardPlayEffect.None;

    /// <summary>
    /// Orichalcum: gaining block is always the argument against it; the End Turn
    /// verdict is simply whether block is zero right now.
    /// </summary>
    public static CardPlayEffect OrichalcumPlay(bool cardGainsBlock) =>
        cardGainsBlock ? CardPlayEffect.Breaks : CardPlayEffect.None;

    public static EndTurnState OrichalcumEndTurn(int currentBlock) =>
        currentBlock == 0 ? EndTurnState.Active : EndTurnState.Broken;

    /// <summary>
    /// Allowance powers (Echo Form, Void Form, ...): the first N matching plays each
    /// turn get the benefit; once the allowance is spent a matching card is X-ed —
    /// it no longer receives the effect.
    /// </summary>
    public static CardPlayEffect AllowancePlay(bool cardMatches, int remaining) =>
        !cardMatches ? CardPlayEffect.None :
        remaining > 0 ? CardPlayEffect.Contributes : CardPlayEffect.Breaks;

    // Projected values: what a counter will read after the selected card is played.
    // Shown in place of the current count while a card is held for play.

    /// <summary>A wrapped counter after one more tick (Pen Nib at 9 reads 0).</summary>
    public static int NextWrapped(int count, int threshold) => (count + 1) % threshold;

    /// <summary>A count-up counter after one more tick.</summary>
    public static int NextUp(int count) => count + 1;

    /// <summary>A countdown after spending one.</summary>
    public static int NextDown(int remaining) => Math.Max(0, remaining - 1);

    /// <summary>A countdown that fires at zero and starts over (Panache).</summary>
    public static int NextCountdownWithReset(int remaining, int resetTo) =>
        remaining <= 1 ? resetTo : remaining - 1;
}
