namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The experimental view switches, toggled live from the combat screen's
/// upper-right panel. Session-only for now, deliberately: these are options being
/// auditioned, not yet settings worth persisting.
/// </summary>
internal static class ViewOptions
{
    /// <summary>The focused card's badges render doubled at the top-center panel
    /// instead of the subtle strip above the card.</summary>
    public static bool BigAndBold;

    /// <summary>Every card in hand carries its badge strip, not just the focused one.</summary>
    public static bool AlwaysOn;

    /// <summary>The strip sits on the card art itself instead of above the card.</summary>
    public static bool CardOverlay;

    /// <summary>Hide the native hand-count digit; the strip absorbs it as a "N."
    /// prefix and moves down over the digit's old spot.</summary>
    public static bool ReplaceCardCount;

    /// <summary>End Turn badges render as a flush extension on top of the End Turn
    /// button instead of floating above it.</summary>
    public static bool ReplaceEndTurn;

    public static event Action? Changed;

    public static void Notify() => Changed?.Invoke();
}
