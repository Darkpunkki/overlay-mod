using System.Reflection;

namespace OverlayMod.Host;

/// <summary>
/// Which build this is, read from the assembly rather than written twice.
///
/// It exists because the first thing any bug report needs is the version, and
/// 0.1.0 shipped without one anywhere a user could see: not in the log, not on
/// the control page, not on the tray icon.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = Read();

    private static string Read()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<commit sha>" when the repository is available.
        // Useful in a log, noise on a control page.
        if (informational is { Length: > 0 })
            return informational.Split('+')[0];

        return typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
