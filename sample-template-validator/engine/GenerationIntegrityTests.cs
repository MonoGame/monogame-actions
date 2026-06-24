using System.Text.RegularExpressions;
using Xunit;

namespace TemplateTester;

/// <summary>
/// Broad generation-correctness checks that apply to any template, derived from template.json.
/// Generates the full platform set once per check and asserts the output is clean and coherent.
/// </summary>
[Collection("template")]
[Trait("Category", "Structural")]
public sealed class GenerationIntegrityTests
{
    private readonly TemplateFixture _fx;
    public GenerationIntegrityTests(TemplateFixture fx) => _fx = fx;

    private static readonly Lazy<TemplateManifest> Manifest =
        new(() => new TemplateManifest(Config.TemplateRoot, Config.PlatformSymbol));

    private string GenerateAll(string name)
        => _fx.Generate(name, Manifest.Value.Platforms.Select(p => p.Token).ToArray());

    [Fact]
    public void No_template_engine_or_vcs_or_build_artifacts()
    {
        var dir = GenerateAll("IntegrityArtifacts");

        Assert.False(Directory.Exists(Path.Combine(dir, ".template.config")),
            ".template.config must not appear in generated output.");
        foreach (var junk in new[] { ".git", ".vs", ".idea" })
            Assert.False(Directory.Exists(Path.Combine(dir, junk)),
                $"{junk}/ must not appear in generated output.");

        var binObj = Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
            .Where(d => Path.GetFileName(d) is "bin" or "obj")
            .Select(d => Path.GetRelativePath(dir, d))
            .ToList();
        Assert.True(binObj.Count == 0, $"bin/obj must not be generated: {string.Join(", ", binObj)}.");
    }

    [Fact]
    public void No_unprocessed_conditional_markers()
    {
        var dir = GenerateAll("IntegrityMarkers");
        foreach (var kv in TextScan.ReadAll(dir, Manifest.Value.CopyOnlyExtensions))
        {
            Assert.False(kv.Value.Contains("<!--#if"),
                $"Unprocessed '<!--#if' marker left in {Path.GetFileName(kv.Key)} - conditional processing didn't run.");
            Assert.False(kv.Value.Contains("<!--#endif"),
                $"Unprocessed '<!--#endif' marker left in {Path.GetFileName(kv.Key)} - conditional processing didn't run.");
        }
    }

    [Fact]
    public void Solution_project_references_all_resolve()
    {
        var dir = GenerateAll("IntegritySolution");
        var solutions = Directory.EnumerateFiles(dir, "*.slnx")
            .Concat(Directory.EnumerateFiles(dir, "*.sln"))
            .ToList();
        if (solutions.Count == 0) return; // template has no solution file

        foreach (var sln in solutions)
        {
            var slnDir = Path.GetDirectoryName(sln)!;
            var text = File.ReadAllText(sln);
            foreach (Match match in Regex.Matches(text, "\"([^\"]+\\.(?:cs|fs|vb|sh)proj)\""))
            {
                var rel = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)
                                               .Replace('/', Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(slnDir, rel));
                Assert.True(File.Exists(full),
                    $"{Path.GetFileName(sln)} references a project that wasn't generated: {match.Groups[1].Value}.");
            }
        }
    }

    [Fact]
    public void Help_lists_every_declared_parameter()
    {
        var help = DotNet.Run(_fx.TemplateRoot, "new", Manifest.Value.ShortName, "--help").Output;
        foreach (var name in Manifest.Value.ParameterNames)
            Assert.Contains(name, help);
    }
}