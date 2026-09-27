namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The experimental view switches, toggled live from the combat screen's
/// upper-right panel.
///
/// Nothing here is persisted, deliberately: these are options being auditioned,
/// and the values below are the ones every player — including the one dialling
/// them in — meets on launch. There is no stored state, so there is nothing a
/// reset could clear. Persist them only once the defaults have settled.
/// </summary>
internal static class ViewOptions
{
    /// <summary>
    /// Size multiplier for the mod's own panels — the focused card's badge display
    /// wherever it is placed, and the setup bar (the slider that replaced the Big
    /// &amp; Bold toggle; that mode was 2.0). Slider range 0.5..3.0 in 0.05 steps,
    /// defaulting to 1.5: at 1.0 the setup bar is legible but not glanceable.
    /// </summary>
    public static float Scale = 1.5f;

    /// <summary>Extend Scale to the surfaces that sit on native UI as well —
    /// unfocused hand strips and the End Turn badges.</summary>
    public static bool ScaleAlwaysOn;

    /// <summary>Show the always-on setup bar: the run-persistent relics, whose
    /// counters carry between combats, with their current counts. On by
    /// default.</summary>
    public static bool ShowSetupRelics = true;

    /// <summary>Anchor the setup bar in the upper left, just under the relic bar
    /// (however many rows it wraps to), instead of the top-center slot it shares
    /// with the "Will trigger:" banner. On by default.</summary>
    public static bool SetupTopLeft = true;

    /// <summary>Show the Block that ending the turn would grant — Orichalcum,
    /// Cloak Clasp, Plating, Frost orbs, ... — as a "+N" shield just left of the
    /// player's native Block shield. On by default.</summary>
    public static bool ShowPendingBlock = true;

    /// <summary>The focused card's badge display moves to the top-center panel
    /// (sized by Scale, never showing the card number).</summary>
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
