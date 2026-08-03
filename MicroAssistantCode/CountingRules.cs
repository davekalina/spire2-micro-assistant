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
}
