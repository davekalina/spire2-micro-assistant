namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The experimental view switches, toggled live from the combat screen's
/// upper-right panel. Session-only for now, deliberately: these are options being
/// auditioned, not yet settings worth persisting.
/// </summary>
internal static class ViewOptions
{
    /// <summary>
    /// Size multiplier for the focused card's badge display, wherever it is placed
    /// (the slider that replaced the Big &amp; Bold toggle; that mode was 2.0).
    /// Slider range 0.5..3.0 in 0.05 steps.
    /// </summary>
    public static float Scale = 1f;

    /// <summary>Apply Scale to every badge surface, not just the focused card's
    /// display — unfocused hand strips, End Turn, and the setup bar too.</summary>
    public static bool ScaleAlwaysOn;

    /// <summary>Show the always-on setup bar: the run-persistent relics, whose
    /// counters carry between combats, with their current counts.</summary>
    public static bool ShowSetupRelics;

    /// <summary>The focused card's badge display moves to the top-center panel
    /// (sized by BigAndBold, never showing the card number).</summary>
    public static bool ShowUpTop;

    /// <summary>Every card in hand carries its badge strip, not just the focused one.</summary>
    public static bool AlwaysOn;

    /// <summary>The strip sits on the card art itself instead of above the card.</summary>
    public static bool CardOverlay;

    /// <summary>Hide the native hand-count digit; the strip absorbs it as a "N."
    /// prefix and moves down over the digit's old spot. On by default: it won its
    /// audition.</summary>
    public static bool ReplaceCardCount = true;

    /// <summary>End Turn badges render as a flush extension on top of the End Turn
    /// button instead of floating above it.</summary>
    public static bool ReplaceEndTurn;

    /// <summary>Hide End Turn badges whose payoff is already lost (the slashed
    /// will-NOT verdicts: Art of War after an attack, Orichalcum with block up,
    /// Pocketwatch past its limit) instead of showing them negated.</summary>
    public static bool HideNegatedPayoffs;

    public static event Action? Changed;

    public static void Notify() => Changed?.Invoke();
}
