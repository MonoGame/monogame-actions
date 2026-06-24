using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TemplateTester;

public static class DotNet
{
    public static (int ExitCode, string Output) Run(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sb = new StringBuilder();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return (p.ExitCode, sb.ToString());
    }

    private static readonly Lazy<string> WorkloadList =
        new(() => Run(AppContext.BaseDirectory, "workload", "list").Output);

    public static bool HasWorkload(string id)
        => Regex.IsMatch(WorkloadList.Value, $@"(?im)^\s*{Regex.Escape(id)}(\s|$)");
}

public static class Config
{
    public static string TemplateRoot { get; } = ResolveRoot();
    public static string PlatformSymbol { get; } = Env("PLATFORM_SYMBOL", "Platforms");
    public static bool IncludeBuild { get; } =
        Env("INCLUDE_BUILD", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string Env(string key, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }

    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("TEMPLATE_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".template.config")))
                return d.FullName;

        throw new InvalidOperationException(
            "Set the TEMPLATE_ROOT environment variable, or run from within a template " +
            "(no .template.config found above the test assembly).");
    }
}

/// <summary>
/// Reads a template's shortName, declared platform choices and the "monogame.platforms" map (token -> { target, folder }), then resolves each declared token to a PlatformChoice.
/// Tokens not in the map fall back to built-in guesses; anything that still cannot map to a known target is reported in <see cref="Unresolved"/>.
/// </summary>
public sealed class TemplateManifest
{
    public string ShortName { get; }
    public string SourceName { get; }
    public IReadOnlyList<string> DeclaredTokens { get; }
    public IReadOnlyList<PlatformChoice> Platforms { get; }   // resolved to a known target
    public IReadOnlyList<string> Unresolved { get; }          // declared but could not be mapped
    public IReadOnlyList<(string Name, string Replaces)> TextParameters { get; }  // text params with a `replaces`
    public IReadOnlyList<string> ParameterNames { get; }                          // all `parameter` symbol names
    public IReadOnlyCollection<string> CopyOnlyExtensions { get; }                // e.g. ".md", ".fbx"

    public TemplateManifest(string templateRoot, string platformSymbol)
    {
        var json = File.ReadAllText(Path.Combine(templateRoot, ".template.config", "template.json"));
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = doc.RootElement;
        ShortName = root.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? "" : "";
        SourceName = root.TryGetProperty("sourceName", out var src) ? src.GetString() ?? "" : "";

        var tokens = new List<string>();
        if (root.TryGetProperty("symbols", out var syms) &&
            syms.TryGetProperty(platformSymbol, out var sym) &&
            sym.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in choices.EnumerateArray())
                if (c.TryGetProperty("choice", out var ch) && ch.GetString() is { } s)
                    tokens.Add(s);
        }
        DeclaredTokens = tokens;

        var map = new Dictionary<string, (string Target, string Folder)>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("monogame", out var mg) && mg.ValueKind == JsonValueKind.Object &&
            mg.TryGetProperty("platforms", out var pf) && pf.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in pf.EnumerateObject())
            {
                var target = prop.Value.TryGetProperty("target", out var t) ? t.GetString() : null;
                var folder = prop.Value.TryGetProperty("folder", out var f) ? f.GetString() : null;
                if (!string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(folder))
                    map[prop.Name] = (target!, folder!);
            }
        }

        var resolved = new List<PlatformChoice>();
        var unresolved = new List<string>();
        foreach (var token in tokens)
        {
            (string Target, string Folder)? info =
                map.TryGetValue(token, out var m) ? m
                : FallbackPlatforms.TryResolve(token, out var fb) ? fb
                : null;

            if (info is null)
                unresolved.Add($"{token} (no monogame.platforms entry, no fallback)");
            else if (!TargetCatalog.IsKnown(info.Value.Target))
                unresolved.Add($"{token} -> {info.Value.Target} (unknown target)");
            else
                resolved.Add(new PlatformChoice(token, info.Value.Folder, info.Value.Target));
        }
        Platforms = resolved;
        Unresolved = unresolved;

        var textParams = new List<(string Name, string Replaces)>();
        var paramNames = new List<string>();
        if (root.TryGetProperty("symbols", out var allSyms) && allSyms.ValueKind == JsonValueKind.Object)
        {
            foreach (var s in allSyms.EnumerateObject())
            {
                var v = s.Value;
                if (v.ValueKind != JsonValueKind.Object) continue;
                if (!(v.TryGetProperty("type", out var ty) && ty.GetString() == "parameter")) continue;
                paramNames.Add(s.Name);
                if (v.TryGetProperty("datatype", out var dt) && dt.GetString() == "text" &&
                    v.TryGetProperty("replaces", out var rp) && rp.GetString() is { Length: > 0 } rs)
                    textParams.Add((s.Name, rs));
            }
        }
        TextParameters = textParams;
        ParameterNames = paramNames;

        var copyExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("sources", out var srcs) && srcs.ValueKind == JsonValueKind.Array)
            foreach (var s in srcs.EnumerateArray())
                if (s.TryGetProperty("modifiers", out var mods) && mods.ValueKind == JsonValueKind.Array)
                    foreach (var mod in mods.EnumerateArray())
                        if (mod.TryGetProperty("copyOnly", out var co) && co.ValueKind == JsonValueKind.Array)
                            foreach (var g in co.EnumerateArray())
                                if (g.GetString() is { } glob)
                                {
                                    var ext = Path.GetExtension(glob);
                                    if (!string.IsNullOrEmpty(ext) && !ext.Contains('*')) copyExt.Add(ext);
                                }
        CopyOnlyExtensions = copyExt;
    }

    public PlatformChoice Find(string token) =>
        Platforms.First(p => p.Token.Equals(token, StringComparison.OrdinalIgnoreCase));
}

public sealed class TemplateFixture : IDisposable
{
    public string TemplateRoot { get; } = Config.TemplateRoot;
    public TemplateManifest Manifest { get; }

    public TemplateFixture()
    {
        Manifest = new TemplateManifest(TemplateRoot, Config.PlatformSymbol);
        if (string.IsNullOrWhiteSpace(Manifest.ShortName))
            throw new InvalidOperationException($"template.json at '{TemplateRoot}' has no shortName.");

        var (code, output) = DotNet.Run(TemplateRoot, "new", "install", TemplateRoot, "--force");
        if (code != 0)
            throw new InvalidOperationException($"`dotnet new install` failed (exit {code}):\n{output}");
    }

    public string Generate(string name, params string[] platforms)
    {
        var (dir, code, output) = TryGenerateRaw(name, ToPlatformArgs(platforms));
        if (code != 0)
            throw new InvalidOperationException(
                $"Generation failed for [{string.Join(",", platforms)}] (exit {code}).\n{output}");
        return dir;
    }

    public (string Dir, int Code, string Output) TryGenerate(string name, params string[] platforms)
        => TryGenerateRaw(name, ToPlatformArgs(platforms));

    public (string Dir, int Code, string Output) TryGenerateRaw(string name, IEnumerable<string> extraArgs)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mg-tmpl-tests", name);
        var args = new List<string> { "new", Manifest.ShortName, "-n", name, "-o", dir };
        args.AddRange(extraArgs);

        var (code, output) = RunNew(dir, args);

        if (code != 0 && output.Contains("No templates", StringComparison.OrdinalIgnoreCase))
        {
            DotNet.Run(TemplateRoot, "new", "install", TemplateRoot, "--force");
            (code, output) = RunNew(dir, args);
        }
        return (dir, code, output);
    }

    private (int Code, string Output) RunNew(string dir, List<string> args)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        return DotNet.Run(TemplateRoot, args.ToArray());
    }

    private IEnumerable<string> ToPlatformArgs(IEnumerable<string> platforms)
    {
        foreach (var p in platforms)
        {
            yield return "--" + Config.PlatformSymbol;
            yield return p;
        }
    }

    public void Dispose() => DotNet.Run(TemplateRoot, "new", "uninstall", TemplateRoot);
}

[CollectionDefinition("template")]
public sealed class TemplateCollection : ICollectionFixture<TemplateFixture> { }

internal static class Naming
{
    public static string ToProjectName(string raw)
    {
        var parts = Regex.Split(raw, "[^A-Za-z0-9]+").Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
        var id = string.Concat(parts);
        if (id.Length == 0 || !char.IsLetter(id[0])) id = "T" + id;
        return id;
    }
}

public static class TextScan
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".vbproj", ".fsproj", ".sln", ".slnx", ".json", ".xml", ".config",
        ".props", ".targets", ".plist", ".manifest", ".xaml", ".axaml", ".resx", ".txt", ".md",
        ".gradle", ".kt", ".java", ".h", ".m", ".mm", ".cpp", ".fx", ".hlsl", ".glsl",
        ".spritefont", ".cfg", ".ini", ".yml", ".yaml", ".strings",
    };

    public static IReadOnlyDictionary<string, string> ReadAll(string root, IReadOnlyCollection<string> skipExtensions)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f)))
            .Where(f => !skipExtensions.Contains(Path.GetExtension(f)))
            .ToDictionary(f => f, File.ReadAllText);
}