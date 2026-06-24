using Xunit;

namespace TemplateTester;

/// <summary>
/// Generic token-replacement check, derived entirely from the template's own template.json:
/// every text parameter with a `replaces` token (and the sourceName) is exercised with a unique sentinel value, then the output is checked to confirm the sentinel landed and the original token didn't linger.
/// No per-sample configuration - the template self-describes what to substitute.
/// </summary>
[Collection("template")]
[Trait("Category", "Structural")]
public sealed class SubstitutionTests
{
    private readonly TemplateFixture _fx;
    public SubstitutionTests(TemplateFixture fx) => _fx = fx;

    private static readonly Lazy<TemplateManifest> Manifest =
        new(() => new TemplateManifest(Config.TemplateRoot, Config.PlatformSymbol));

    [Fact]
    public void Replace_parameters_and_sourceName_substitute()
    {
        var m = Manifest.Value;
        var hasSource = !string.IsNullOrEmpty(m.SourceName);
        if (!hasSource && m.TextParameters.Count == 0)
            return;

        const string nameSentinel = "ZsubName000";
        var paramSentinels = m.TextParameters
            .Select((p, i) => (p.Name, p.Replaces, Sentinel: $"ZsubP{i}Val"))
            .ToList();

        var args = new List<string>();
        foreach (var p in m.Platforms) { args.Add("--" + Config.PlatformSymbol); args.Add(p.Token); }
        foreach (var p in paramSentinels) { args.Add("--" + p.Name); args.Add(p.Sentinel); }

        var (dir, code, output) = _fx.TryGenerateRaw(nameSentinel, args);
        Assert.True(code == 0, $"Generation for the substitution check failed.\n{output}");

        var contents = TextScan.ReadAll(dir, m.CopyOnlyExtensions);

        foreach (var p in paramSentinels)
            Assert.True(contents.Values.Any(c => c.Contains(p.Sentinel)),
                $"--{p.Name} (replaces \"{p.Replaces}\") did not substitute: '{p.Sentinel}' not found in any generated file.");

        foreach (var p in paramSentinels)
            foreach (var kv in contents)
                Assert.False(kv.Value.Contains(p.Replaces),
                    $"Leftover token \"{p.Replaces}\" in {Path.GetFileName(kv.Key)} after setting --{p.Name}.");

        if (hasSource)
        {
            var stillNamed = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Contains(m.SourceName, StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToList();
            Assert.True(stillNamed.Count == 0,
                $"sourceName \"{m.SourceName}\" still present in path name(s): {string.Join(", ", stillNamed)}.");

            foreach (var kv in contents)
                Assert.False(kv.Value.Contains(m.SourceName),
                    $"Leftover sourceName \"{m.SourceName}\" in {Path.GetFileName(kv.Key)}.");
        }
    }
}