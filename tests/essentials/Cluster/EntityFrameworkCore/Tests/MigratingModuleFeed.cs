namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The migrating fixture module packed in two generations by the SDK, as an operator's package pipeline would publish two
/// releases: generation 1 (version 1.0.0) carries one migration, generation 2 (2.0.0) that one and a second. Each is packed by
/// <see cref="FoundationHostFeed.PackReleaseAsync"/>, as every fixture with several releases is.
/// </summary>
public sealed class MigratingModuleFeed : IAsyncLifetime
{
    private static readonly string Project = Path.Join(FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "Fixtures", "MigratingModule", "Elsa.Cluster.Fixtures.MigratingModule.csproj");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("elsa-migrating-module-feed-");

    /// <summary>The <c>.nupkg</c> of <paramref name="generation"/> of the module: 1 or 2.</summary>
    public string Package(int generation) => Directory.EnumerateFiles(Path.Join(_root.FullName, "packed", generation.ToString()), "*.nupkg").Single();

    public async Task InitializeAsync()
    {
        foreach (var generation in new[] { 1, 2 })
            await FoundationHostFeed.PackReleaseAsync(
                Project,
                Path.Join(_root.FullName, "build", generation.ToString()),
                Path.Join(_root.FullName, "packed", generation.ToString()),
                $"MigratingModuleGeneration={generation}");
    }

    public Task DisposeAsync()
    {
        _root.Delete(recursive: true);
        return Task.CompletedTask;
    }
}
