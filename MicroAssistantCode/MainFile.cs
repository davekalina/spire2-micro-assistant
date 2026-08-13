using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace MicroAssistant.MicroAssistantCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    /// <summary>
    /// The mod's permanent identity. It must equal the manifest id and the assembly name,
    /// because the loader loads "&lt;id&gt;.dll" from "&lt;game&gt;/mods/&lt;id&gt;/".
    /// Changing it after publishing orphans the Workshop item.
    /// </summary>
    public const string ModId = "MicroAssistant";

    public const string ModName = "Micro Assistant";

    /// <summary>Keep in sync with the <c>version</c> field in MicroAssistant.json.</summary>
    public const string Version = "v0.13.1";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        // PatchAll is deliberately not guarded. A patch whose target has moved means
        // this build does not fit this game, and the loader's own "failed to
        // initialize" is the honest report — better than a mod that loads and then
        // misbehaves in ways the player has to diagnose.
        new Harmony(ModId).PatchAll();
        Logger.Info($"{ModName} {Version} initialized.");
    }
}
