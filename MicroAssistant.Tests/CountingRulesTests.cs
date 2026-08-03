using MicroAssistant.MicroAssistantCode;
using Xunit;

namespace MicroAssistant.Tests;

public class CountingRulesTests
{
    // Pocketwatch: draw 3 next turn if at most 3 cards are played this turn.
    // Counts already played this turn: 0..2 leave room, 3 means the next play breaks it.
    [Theory]
    [InlineData(0, CardPlayEffect.Contributes)]
    [InlineData(2, CardPlayEffect.Contributes)]
    [InlineData(3, CardPlayEffect.Breaks)]
    [InlineData(7, CardPlayEffect.Breaks)]
    public void Pocketwatch_card_play(int cardsPlayed, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.PocketwatchPlay(cardsPlayed, cardThreshold: 3));

    [Theory]
    [InlineData(0, EndTurnState.Active)]
    [InlineData(3, EndTurnState.Active)]
    [InlineData(4, EndTurnState.Broken)]
    public void Pocketwatch_end_turn(int cardsPlayed, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.PocketwatchEndTurn(cardsPlayed, cardThreshold: 3));

    // Pendulum: no card play ever shows it; it earns the End Turn slot only on the
    // turn whose end brings the draw (counter at cycle length - 1).
    [Fact]
    public void Pendulum_ignores_card_plays() =>
        Assert.Equal(CardPlayEffect.None, CountingRules.PendulumPlay());

    [Theory]
    [InlineData(0, EndTurnState.Hidden)]
    [InlineData(1, EndTurnState.Hidden)]
    [InlineData(2, EndTurnState.Active)]
    public void Pendulum_end_turn(int turnsSeen, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.PendulumEndTurn(turnsSeen, turns: 3));

    // Pen Nib: attacks contribute, nothing breaks it, and it never claims the
    // End Turn slot because ending the turn does not move its count.
    [Theory]
    [InlineData(true, CardPlayEffect.Contributes)]
    [InlineData(false, CardPlayEffect.None)]
    public void Pen_nib_card_play(bool isAttack, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.PenNibPlay(isAttack));

    [Fact]
    public void Pen_nib_never_shows_on_end_turn() =>
        Assert.Equal(EndTurnState.Hidden, CountingRules.PenNibEndTurn());

    [Theory]
    [InlineData(8, 10, false)]
    [InlineData(9, 10, true)]
    [InlineData(0, 10, false)]
    [InlineData(2, 3, true)]
    public void Primed_means_next_tick_triggers(int count, int threshold, bool expected) =>
        Assert.Equal(expected, CountingRules.IsPrimed(count, threshold));
}
