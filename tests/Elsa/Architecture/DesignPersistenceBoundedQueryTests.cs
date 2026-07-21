using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// T049: a regression ratchet that fails when any of the removed transitional load-all artifacts
/// — the by-collection enumeration route, the in-memory query evaluator, or the transitional
/// generic read store — reappears in the design persistence lane. It pins the removed surface by
/// exact token; certification of newly written scale-bearing paths is owned by the provider
/// admission layer (undeclared shapes fail before I/O) and the provider plan contract suite.
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
                // Querying registry wires the runtime routes alongside the design writer, so only the
                // exact runtime-manifest-qualified member references are stripped before scanning; any
                // other forbidden token on the same line still fails the gate.
                var content = File.ReadAllText(file)
                    .Replace("ElsaRuntimeStorageManifest.ListAllQuery", "<runtime-qualified>", StringComparison.Ordinal)
                    .Replace("ElsaRuntimeStorageManifest.ByCollectionIndex", "<runtime-qualified>", StringComparison.Ordinal);
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
