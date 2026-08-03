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

    // Every-Nth tickers: only matching cards speak, nothing breaks.
    [Theory]
    [InlineData(true, CardPlayEffect.Contributes)]
    [InlineData(false, CardPlayEffect.None)]
    public void Tickers_contribute_on_matching_cards(bool matches, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.TickerPlay(matches));

    // Brilliant Scarf: contributes up to the free 5th card, silent once spent.
    [Theory]
    [InlineData(0, CardPlayEffect.Contributes)]
    [InlineData(4, CardPlayEffect.Contributes)]
    [InlineData(5, CardPlayEffect.None)]
    [InlineData(9, CardPlayEffect.None)]
    public void Scarf_window(int played, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.ScarfPlay(played, threshold: 5));

    // Velvet Choker: the 7th card is refused.
    [Theory]
    [InlineData(5, CardPlayEffect.Contributes)]
    [InlineData(6, CardPlayEffect.Breaks)]
    public void Choker_cap(int played, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.ChokerPlay(played, limit: 6));

    // Art of War / Ripple Basin: attacks always argue against the payoff.
    [Theory]
    [InlineData(true, CardPlayEffect.Breaks)]
    [InlineData(false, CardPlayEffect.None)]
    public void Avoidance_play(bool isMatch, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.AvoidancePlay(isMatch));

    [Theory]
    [InlineData(true, EndTurnState.Active)]
    [InlineData(false, EndTurnState.Broken)]
    public void Avoidance_end_turn(bool intact, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.AvoidanceEndTurn(intact));

    // Pael's Eye speaks only while the zero-card turn is still achievable.
    [Theory]
    [InlineData(true, CardPlayEffect.Breaks, EndTurnState.Active)]
    [InlineData(false, CardPlayEffect.None, EndTurnState.Hidden)]
    public void Paels_eye(bool intact, CardPlayEffect play, EndTurnState endTurn)
    {
        Assert.Equal(play, CountingRules.PaelsEyePlay(intact));
        Assert.Equal(endTurn, CountingRules.PaelsEyeEndTurn(intact));
    }

    // Rainbow Ring: only unfilled types contribute, and only before it activated.
    [Theory]
    [InlineData(true, false, CardPlayEffect.Contributes)]
    [InlineData(false, false, CardPlayEffect.None)]
    [InlineData(true, true, CardPlayEffect.None)]
    public void Rainbow_ring(bool missing, bool activated, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.RainbowPlay(missing, activated));

    // Pollinous Core counts without wrapping: primed at or beyond the cycle edge.
    [Theory]
    [InlineData(2, EndTurnState.Hidden)]
    [InlineData(3, EndTurnState.Active)]
    [InlineData(5, EndTurnState.Active)]
    public void Pollinous_end_turn(int turnsSeen, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.PollinousEndTurn(turnsSeen, turns: 4));

    [Theory]
    [InlineData(6, EndTurnState.Hidden)]
    [InlineData(7, EndTurnState.Active)]
    [InlineData(8, EndTurnState.Hidden)]
    public void Stone_calendar_fires_on_its_turn(int turn, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.StoneCalendarEndTurn(turn, damageTurn: 7));

    // First-match triggers: armed shows, spent X-es per-turn sources only.
    [Theory]
    [InlineData(false, true, true, CardPlayEffect.None)]
    [InlineData(true, true, true, CardPlayEffect.Contributes)]
    [InlineData(true, false, true, CardPlayEffect.Breaks)]
    [InlineData(true, false, false, CardPlayEffect.None)]
    public void First_match_play(bool matches, bool armed, bool spentX, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.FirstMatchPlay(matches, armed, spentX));

    // Orichalcum: block cards always argue against it; verdict is block == 0.
    [Theory]
    [InlineData(true, CardPlayEffect.Breaks)]
    [InlineData(false, CardPlayEffect.None)]
    public void Orichalcum_play(bool gainsBlock, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.OrichalcumPlay(gainsBlock));

    [Theory]
    [InlineData(0, EndTurnState.Active)]
    [InlineData(1, EndTurnState.Broken)]
    [InlineData(20, EndTurnState.Broken)]
    public void Orichalcum_end_turn(int block, EndTurnState expected) =>
        Assert.Equal(expected, CountingRules.OrichalcumEndTurn(block));

    // Allowance powers: matching cards contribute while charges remain, X at zero.
    [Theory]
    [InlineData(false, 2, CardPlayEffect.None)]
    [InlineData(true, 2, CardPlayEffect.Contributes)]
    [InlineData(true, 0, CardPlayEffect.Breaks)]
    public void Allowance_play(bool matches, int remaining, CardPlayEffect expected) =>
        Assert.Equal(expected, CountingRules.AllowancePlay(matches, remaining));
}
