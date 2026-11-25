using System.Reflection;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.HoverTips;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The places where the game's current builds disagree, answered for all of them, so one
/// DLL works on 0.111 (main, and this mod's min_game_version) and on 0.114 (the current
/// beta) alike, and stays harmless on 0.113 (the previous beta).
///
/// Each signature member here exists because a direct call compiled against one build
/// fails on the other. That failure is not a compile error anyone sees. It is a
/// <c>MissingMethodException</c> thrown the first time the calling method runs, inside
/// <see cref="Guard" />, so all anyone sees is a feature that quietly stopped working.
/// Anything that differs between builds goes through here, by name, and takes whichever
/// form the running game has. Rule changes that no signature shows are decided here by
/// the running game's version.
/// </summary>
internal static class GameCompat
{
    // ---- HoverTip.Id ------------------------------------------------------------------
    //
    // 0.111's HoverTip.Id has a setter; 0.113 made it get-only (still so on 0.114). A
    // compiled `tip.Id = …` calls set_Id, which no longer exists there. All keep it as an
    // auto-property, so the compiler-generated backing field is there either way; the
    // setter is preferred while it exists.

    private static readonly MethodInfo? TipIdSetter =
        typeof(HoverTip).GetProperty(nameof(HoverTip.Id))?.GetSetMethod(nonPublic: true);
    private static readonly FieldInfo? TipIdField =
        typeof(HoverTip).GetField($"<{nameof(HoverTip.Id)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Sets the Id on a boxed <see cref="HoverTip" />. The Id only decides which tips
    /// a tip list treats as duplicates, so when neither form is found the tip keeps the
    /// Id its constructor gave it and still shows.
    /// </summary>
    public static void SetHoverTipId(object boxedTip, string id)
    {
        if (TipIdSetter is not null)
            TipIdSetter.Invoke(boxedTip, [id]);
        else
            TipIdField?.SetValue(boxedTip, id);
    }

    // ---- Unmovable's count -----------------------------------------------------------
    //
    // UnmovablePower.ModifyBlockMultiplicative counts this turn's card-play Block gains
    // against its Amount. 0.111 counts every BlockGainedEntry; 0.114 counts the distinct
    // card plays among them (DistinctBy CardPlay). Same signature, so only the version
    // tells them apart: the game's own ReleaseInfoManager.SemVer, the version its mod
    // loader checks min_game_version against. Without one (a non-release build) the
    // newer rule is assumed.

    private static bool? _unmovableDistinct;

    /// <summary>Whether Unmovable spends its allowance once per card play rather than per Block gain.</summary>
    public static bool UnmovableCountsDistinctPlays => _unmovableDistinct ??= ReadUnmovableRule();

    private static bool ReadUnmovableRule()
    {
        try
        {
            var version = ReleaseInfoManager.Instance.SemVer;
            return version is null ||
                CountingRules.UnmovableCountsDistinctPlays(version.Major, version.Minor);
        }
        catch (Exception)
        {
            return true;
        }
    }
}
