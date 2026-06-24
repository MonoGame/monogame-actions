using Xunit;

namespace TemplateTester;

/// <summary>
/// Compiles a generated single-platform project per supported target. The theory data only yields platforms this host can actually build (the target's host-OS + workload rules). 
/// Skipped entirely when INCLUDE_BUILD=false.
/// Run just these with: dotnet test --filter Category=Build
/// </summary>
[Collection("template")]
[Trait("Category", "Build")]
public sealed class BuildTests
{
    private readonly TemplateFixture _fx;
    public BuildTests(TemplateFixture fx) => _fx = fx;

    private static readonly Lazy<TemplateManifest> Manifest =
        new(() => new TemplateManifest(Config.TemplateRoot, Config.PlatformSymbol));

    public static IEnumerable<object[]> BuildablePlatforms()
    {
        if (!Config.IncludeBuild) yield break;
        foreach (var p in Manifest.Value.Platforms)
            if (TargetCatalog.BuildableOnThisHost(p.Target))
                yield return new object[] { p.Token };
    }

    [Theory]
    [MemberData(nameof(BuildablePlatforms))]
    public void Builds(string token)
    {
        var p = Manifest.Value.Find(token);
        var dir = _fx.Generate(Naming.ToProjectName("Build-" + token), token);

        var solution = Directory.EnumerateFiles(dir, "*.slnx")
            .Concat(Directory.EnumerateFiles(dir, "*.sln"))
            .FirstOrDefault();
        var csproj = Directory.EnumerateFiles(Path.Combine(dir, p.Folder), "*.csproj").FirstOrDefault();
        Assert.True(csproj != null, $"No .csproj found in {p.Folder}/ for '{token}'.");

        var restoreTarget = solution ?? csproj!;
        var (restoreCode, restoreOut) = DotNet.Run(dir, "restore", restoreTarget);
        Assert.True(restoreCode == 0, $"Restore failed for '{token}' (target {p.Target}).\n{restoreOut}");

        var (code, output) = DotNet.Run(dir, "build", csproj!, "--no-restore", "-clp:ErrorsOnly", "--nologo");
        Assert.True(code == 0, $"Build failed for '{token}' (target {p.Target}).\n{output}");
    }
}