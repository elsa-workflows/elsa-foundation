using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Phase 4 (#1631): Secrets Groundwork matrix and ledger obligations are owned by the
/// Groundwork-selected composition. They must not block an EF-selected shell, and the
/// Groundwork-selected path must keep its own coverage.
/// </summary>
public sealed class SecretsPersistenceGateOwnershipTests
{
    private static readonly string[] GroundworkOwnedTestRoots =
    [
        "tests/Elsa/Secrets/Persistence/Groundwork/V2/Tests/Elsa.Secrets.Persistence.Groundwork.V2.Tests.csproj",
        "tests/Elsa/Secrets/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj"
    ];

    private static readonly string[] EntityFrameworkOwnedTestRoots =
    [
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj"
    ];

    [Fact]
    public void Ci_runs_ef_and_groundwork_secrets_gates_as_independent_jobs()
    {
        var jobs = LoadWorkflowJobs(RepoPath(".github", "workflows", "ci.yml"));
        var ef = JobText(jobs, "secrets-ef-composition");
        var groundwork = JobText(jobs, "groundwork-v2-native-provider-matrix");
        var alert = JobText(jobs, "alert");

        Assert.Contains("Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests", ef, StringComparison.Ordinal);
        Assert.DoesNotContain("GroundworkV2SecretsProviderMatrixTests", ef, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Secrets.Persistence.Groundwork", ef, StringComparison.Ordinal);

        Assert.Contains("GroundworkV2SecretsProviderMatrixTests", groundwork, StringComparison.Ordinal);
        Assert.Contains("Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests", groundwork, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests", groundwork, StringComparison.Ordinal);
        Assert.DoesNotContain("Secrets EF composition on PostgreSQL", groundwork, StringComparison.Ordinal);

        Assert.Contains("secrets-ef-composition", alert, StringComparison.Ordinal);
        Assert.Contains("groundwork-v2-native-provider-matrix", alert, StringComparison.Ordinal);
    }

    [Fact]
    public void Groundwork_and_ef_secrets_test_projects_both_remain()
    {
        foreach (var relative in GroundworkOwnedTestRoots.Concat(EntityFrameworkOwnedTestRoots))
            Assert.True(File.Exists(RepoPath(relative.Split('/'))), $"{relative} must remain; Phase 4 quarantines ownership, it does not delete a persistence mode.");
    }

    [Fact]
    public void Ledger_call_down_names_only_secrets_repository()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(RepoPath("specs", "094-harden-groundwork-stores", "coverage-ledger.json")));
        var conditional = document.RootElement.GetProperty("compositionConditionalEntries");
        Assert.Equal(1, conditional.GetArrayLength());
        var entry = conditional[0];
        Assert.Equal("secrets-repository", entry.GetProperty("entryId").GetString());
        Assert.Equal("SecretsGroundworkPersistence", entry.GetProperty("requiredWhenFeature").GetString());
        Assert.Equal("SecretsEntityFrameworkCore", entry.GetProperty("omittedWhenFeature").GetString());
        Assert.False(
            document.RootElement.GetProperty("entries").EnumerateArray()
                .Single(candidate => candidate.GetProperty("id").GetString() == "secrets-repository")
                .GetProperty("compositionOwnership").GetProperty("universalPrerequisite").GetBoolean());

        var defaultCovered = document.RootElement.GetProperty("compositionEvidence").GetProperty("coveredEntryIds")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.Contains("secrets-repository", defaultCovered);
        Assert.Contains("runtime-checkpoint-commit", defaultCovered);
        Assert.Contains("runtime-scheduler-work-queue", defaultCovered);
        Assert.Contains("distributed-execution-placement", defaultCovered);
    }

    [Fact]
    public void Default_workbench_shells_keep_groundwork_secrets()
    {
        foreach (var relative in new[]
                 {
                     "src/Apps/Elsa.Workbench/shells.json",
                     "docker/compose/elsa-workbench.shells.json"
                 })
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(RepoPath(relative.Split('/'))),
                new System.Text.Json.JsonDocumentOptions
                {
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            var features = document.RootElement
                .GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("SecretsGroundworkPersistence", features);
            Assert.DoesNotContain("SecretsEntityFrameworkCore", features);
        }
    }

    private static YamlMappingNode LoadWorkflowJobs(string path)
    {
        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = Assert.IsType<YamlMappingNode>(yaml.Documents[0].RootNode);
        return Assert.IsType<YamlMappingNode>(root.Children[new YamlScalarNode("jobs")]);
    }

    private static string JobText(YamlMappingNode jobs, string jobName)
    {
        Assert.True(
            jobs.Children.TryGetValue(new YamlScalarNode(jobName), out var node),
            $"CI workflow is missing job '{jobName}'.");
        return string.Join('\n', ScalarValues(node));
    }

    private static IEnumerable<string> ScalarValues(YamlNode node) => node switch
    {
        YamlScalarNode scalar when scalar.Value is not null => [scalar.Value],
        YamlMappingNode mapping => mapping.Children.SelectMany(pair => ScalarValues(pair.Key).Concat(ScalarValues(pair.Value))),
        YamlSequenceNode sequence => sequence.Children.SelectMany(ScalarValues),
        _ => []
    };

    private static string RepoPath(params string[] segments) => Path.Join([RepoRoot, ..segments]);

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
