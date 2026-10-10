using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps the Foundation host's container build in step with its project graph.
/// </summary>
public sealed class HostEndpointMetadataTests
{
    /// <summary>
    /// Every project the host restores reaches the restore layer with its committed lock file (#2122): the restores are
    /// locked, and a locked restore of a project with no lock file writes one and carries on, unlocked, without a word.
    /// </summary>
    [Fact]
    public void Foundation_host_Dockerfile_includes_its_project_reference_graph()
    {
        var projectPath = Path.Combine(RepoRoot, "src", "apps", "Elsa.Foundation.Host", "Elsa.Foundation.Host.csproj");
        var dockerfile = ReadSource("src/apps/Elsa.Foundation.Host/Dockerfile");
        var projectReferences = DiscoverProjectReferences(projectPath);

        Assert.NotEmpty(projectReferences);
        foreach (var restoredProjectPath in projectReferences.Append(Path.GetFullPath(projectPath)))
        {
            var relativeProjectPath = Path.GetRelativePath(RepoRoot, restoredProjectPath).Replace('\\', '/');
            var relativeProjectDirectory = Path.GetDirectoryName(relativeProjectPath)!.Replace('\\', '/');

            Assert.Contains($"COPY {relativeProjectPath} {relativeProjectDirectory}/packages.lock.json {relativeProjectDirectory}/", dockerfile, StringComparison.Ordinal);
            Assert.Contains($"COPY {relativeProjectDirectory}/ {relativeProjectDirectory}/", dockerfile, StringComparison.Ordinal);
        }
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyCollection<string> DiscoverProjectReferences(string rootProjectPath)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(Path.GetFullPath(rootProjectPath));

        while (pending.TryDequeue(out var projectPath))
        {
            foreach (var referencedProjectPath in ProjectGraph.ReferencedProjectPaths(projectPath))
            {
                if (discovered.Add(referencedProjectPath))
                    pending.Enqueue(referencedProjectPath);
            }
        }

        return discovered;
    }
}
