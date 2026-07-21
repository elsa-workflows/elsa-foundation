using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// T049: fails when a transitional load-all/client-evaluated query path returns to the design
/// persistence lane. Every scale-bearing design read must execute through a declared shaped
/// bounded route; the by-collection enumeration route, the in-memory query evaluator, and the
/// transitional generic read store were removed by Spec 093 US2 and must not reappear.
/// </summary>
public sealed class DesignPersistenceBoundedQueryTests
{
    /// <summary>Design persistence source directories covered by the bounded-query guarantee.</summary>
    private static readonly string[] GuardedDirectories =
    [
        "src/Elsa/Persistence/Groundwork/Querying",
        "src/Elsa/Workflows/Design/Persistence/Groundwork",
        "src/Elsa/Activities/Design/Persistence/Groundwork",
        "src/Elsa/Workflows/Publishing/Persistence/Groundwork"
    ];

    /// <summary>
    /// Tokens whose presence marks a load-all fallback or uncertified client evaluation. The
    /// list-all/by-collection identities remain legal in runtime-owned manifests, but design
    /// sources may not reference them directly or through the runtime constants.
    /// </summary>
    private static readonly string[] ForbiddenTokens =
    [
        "InMemoryQueryEvaluator",
        "GroundworkReadStore",
        "\"list-all\"",
        "\"by-collection\"",
        "ListAllQuery",
        "ByCollectionIndex",
        "ListAllAsync"
    ];

    [Fact]
    public void Design_persistence_sources_contain_no_load_all_or_client_evaluation_path()
    {
        var violations = new List<string>();
        foreach (var directory in GuardedDirectories)
        {
            var path = FullPath(directory);
            Assert.True(Directory.Exists(path), $"Guarded design directory '{directory}' does not exist.");

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                // The list-all/by-collection identities remain legal in runtime-owned manifests. The shared
                // Querying registry wires the runtime routes alongside the design writer, so a line that
                // qualifies the identity with the runtime manifest is not a design-lane fallback.
                var designLines = File.ReadLines(file)
                    .Where(line => !line.Contains("ElsaRuntimeStorageManifest", StringComparison.Ordinal));
                var content = string.Join('\n', designLines);
                violations.AddRange(ForbiddenTokens
                    .Where(token => content.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(RepoRoot, file)}: {token}"));
            }
        }

        Assert.True(
            violations.Count == 0,
            "Design persistence sources reintroduced a load-all/client-evaluation path:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_transitional_reader_and_in_memory_evaluator_stay_deleted()
    {
        Assert.False(
            File.Exists(FullPath("src/Elsa/Persistence/Groundwork/Querying/GroundworkReadStore.cs")),
            "The transitional GroundworkReadStore must stay deleted.");
        Assert.False(
            File.Exists(FullPath("src/Elsa/Persistence/Core/Queries/InMemoryQueryEvaluator.cs")),
            "The InMemoryQueryEvaluator must stay deleted; bounded provider routes own all design query execution.");
    }

    private static string FullPath(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
