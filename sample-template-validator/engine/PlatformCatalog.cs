namespace TemplateTester;

/// <summary>
/// Which host OS can compile a given target.
/// </summary>
public enum HostRequirement { Any, Windows, MacOS }

/// <summary>
/// Intrinsic build constraints for a canonical MonoGame target.
/// </summary>
public sealed record TargetRules(HostRequirement Host, string? Workload);

/// <summary>
/// A platform choice resolved for the template under test: the CLI token (the choice value), the source folder its code lives in, and the canonical MonoGame target it maps to.
/// </summary>
public sealed record PlatformChoice(string Token, string Folder, string Target);

/// <summary>
/// The intrinsic build rules per canonical MonoGame target (which host OS / workload can compile it).
/// This is the ONLY platform knowledge the engine hard-codes - it is sample-agnostic. Templates map their own token -> { target, folder } via the "monogame.platforms" block; the engine looks the build rules up here by target.
/// Extend this table when MonoGame gains or renames a backend.
/// </summary>
public static class TargetCatalog
{
    public static readonly IReadOnlyDictionary<string, TargetRules> All =
        new Dictionary<string, TargetRules>(StringComparer.OrdinalIgnoreCase)
        {
            ["DesktopGL"]   = new(HostRequirement.Any,     null),
            ["DesktopVK"]   = new(HostRequirement.Any,     null),
            ["WindowsDX"]   = new(HostRequirement.Windows, null),
            ["WindowsDX12"] = new(HostRequirement.Windows, null),
            ["Android"]     = new(HostRequirement.Any,     "android"),
            ["iOS"]         = new(HostRequirement.MacOS,   "ios"),
        };

    public static bool IsKnown(string target) => All.ContainsKey(target);

    public static bool BuildableOnThisHost(string target)
    {
        if (!All.TryGetValue(target, out var r)) return false;
        var osOk = r.Host switch
        {
            HostRequirement.Any     => true,
            HostRequirement.Windows => OperatingSystem.IsWindows(),
            HostRequirement.MacOS   => OperatingSystem.IsMacOS(),
            _ => false,
        };
        return osOk && (r.Workload is null || DotNet.HasWorkload(r.Workload));
    }
}

/// <summary>
/// Built-in token -> { target, folder } guesses, used ONLY when a template does not declare its own "monogame.platforms" map.
/// New samples should declare the map explicitly; this just keeps older templates working.
/// </summary>
public static class FallbackPlatforms
{
    // COnsider adding console platforms if/when they are supported by the template.
    private static readonly Dictionary<string, (string Target, string Folder)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["desktopgl"]   = ("DesktopGL",   "DesktopGL"),
            ["desktopvk"]   = ("DesktopVK",   "DesktopVK"),
            ["windowsdx"]   = ("WindowsDX",   "WindowsDX"),
            ["windowsdx12"] = ("WindowsDX12", "WindowsDX12"),
            ["android"]     = ("Android",     "Android"),
            ["ios"]         = ("iOS",         "iOS"),
        };

    public static bool TryResolve(string token, out (string Target, string Folder) info)
        => Map.TryGetValue(token, out info);
}