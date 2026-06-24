using Xunit;

namespace TemplateTester;

/// <summary>
/// Auto-derived from the template under test:
/// for every platform it declares (resolved via its monogame.platforms map to a known target), verify it isolates correctly, that all-together generates, and that bad/empty input behaves. Pure `dotnet new`, 
/// so these run on every OS (Linux's case-sensitivity also catches folder/symbol casing slips that case-insensitive filesystems hide).
/// </summary>
[Collection("template")]
[Trait("Category", "Structural")]
public sealed class PlatformTests
{
    private readonly TemplateFixture _fx;
    public PlatformTests(TemplateFixture fx) => _fx = fx;

    private static readonly Lazy<TemplateManifest> Manifest =
        new(() => new TemplateManifest(Config.TemplateRoot, Config.PlatformSymbol));

    public static IEnumerable<object[]> SupportedTokens()
        => Manifest.Value.Platforms.Select(p => new object[] { p.Token });

    [Fact]
    public void All_declared_platforms_resolve_to_a_known_target()
    {
        Assert.True(Manifest.Value.Unresolved.Count == 0,
            "Template declares platform(s) the framework can't map to a known target: " +
            string.Join("; ", Manifest.Value.Unresolved) +
            ". Add a monogame.platforms entry (target + folder), or extend the target catalog.");
    }

    [Theory]
    [MemberData(nameof(SupportedTokens))]
    public void Single_platform_isolates_correctly(string token)
    {
        var me = Manifest.Value.Find(token);
        var dir = _fx.Generate(Naming.ToProjectName("Only-" + token), token);

        Assert.True(Directory.Exists(Path.Combine(dir, me.Folder)),
            $"Selecting '{token}' should produce {me.Folder}/.");

        var otherFolders = Manifest.Value.Platforms
            .Where(p => !p.Token.Equals(token, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Folder)
            .Where(f => !f.Equals(me.Folder, StringComparison.OrdinalIgnoreCase))
            .Distinct();

        foreach (var folder in otherFolders)
            Assert.False(Directory.Exists(Path.Combine(dir, folder)),
                $"Selecting only '{token}' should exclude {folder}/.");

        var slnx = Directory.EnumerateFiles(dir, "*.slnx").FirstOrDefault();
        if (slnx != null)
        {
            var text = File.ReadAllText(slnx);
            Assert.Contains(me.Folder + "/", text);
            foreach (var folder in otherFolders)
                Assert.DoesNotContain(folder + "/", text);
        }
    }

    [Fact]
    public void All_supported_platforms_generate_together()
    {
        var platforms = Manifest.Value.Platforms;
        if (platforms.Count == 0) return;

        var dir = _fx.Generate("AllPlatforms", platforms.Select(p => p.Token).ToArray());
        foreach (var p in platforms)
            Assert.True(Directory.Exists(Path.Combine(dir, p.Folder)),
                $"All-platforms generation should include {p.Folder}/.");
    }

    [Fact]
    public void Invalid_platform_is_rejected()
    {
        var (_, code, _) = _fx.TryGenerate("InvalidPlatform", "definitely-not-a-platform");
        Assert.True(code != 0, "An unknown platform value should make generation fail.");
    }

    [Fact]
    public void Default_generation_succeeds()
    {
        var (_, code, output) = _fx.TryGenerate("DefaultGeneration");
        Assert.True(code == 0, $"Generation with no platform args should succeed.\n{output}");
    }
}