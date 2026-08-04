namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// One-time INFO breadcrumbs at the seams the overlay depends on. The mod is
/// otherwise silent on success, which made "works here, nothing there" impossible
/// to localize from a log; each key logs its first occurrence per session so a
/// single fight shows exactly how far the chain gets.
/// </summary>
internal static class Diag
{
    private static readonly HashSet<string> Logged = [];

    public static void Once(string key, Func<string> message)
    {
        if (Logged.Add(key))
            MainFile.Logger.Info($"[diag] {message()}");
    }
}
