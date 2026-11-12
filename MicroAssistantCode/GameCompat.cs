using System.Reflection;
using MegaCrit.Sts2.Core.HoverTips;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The places where the game's current builds disagree, answered for all of them, so one
/// DLL loads on the default branch (0.111) and on the beta (0.113) alike.
///
/// Each member here exists because a direct call compiled against one build fails on
/// the other. That failure is not a compile error anyone sees. It is a
/// <c>MissingMethodException</c> thrown the first time the calling method runs, inside
/// <see cref="Guard" />, so all anyone sees is a feature that quietly stopped working.
/// Anything that differs between builds goes through here, by name, and takes whichever
/// form the running game has.
/// </summary>
internal static class GameCompat
{
    // ---- HoverTip.Id ------------------------------------------------------------------
    //
    // 0.111's HoverTip.Id has a setter; 0.113 made it get-only. A compiled `tip.Id = …`
    // calls set_Id, which no longer exists on 0.113. Both builds keep it as an
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
}
