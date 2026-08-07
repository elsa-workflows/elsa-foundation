using Elsa.Persistence.Groundwork.Targets;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Microsoft.Data.Sqlite;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// The portfolio tile counts design-lane definitions and drafts plus how many are published, which is a
/// runtime-lane fact. Co-located, that is one statement; split, no connection can see both lanes, so the
/// counts have to agree with the single-statement path via in-memory correlation. Issue #1156.
/// </summary>
public sealed class SplitPortfolioDataSourceTests : IAsyncLifetime
{
    private readonly string _designPath = Path.Combine(Path.GetTempPath(), $"portfolio-design-{Guid.NewGuid():N}.db");
    private readonly string _runtimePath = Path.Combine(Path.GetTempPath(), $"portfolio-runtime-{Guid.NewGuid():N}.db");
    private readonly string _sharedPath = Path.Combine(Path.GetTempPath(), $"portfolio-shared-{Guid.NewGuid():N}.db");

    private static readonly DateTimeOffset AsOf = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private const string TenantId = "tenant-a";

    public async Task InitializeAsync()
    {
        // Two published definitions, one unpublished, and one draft against a published definition.
        await SeedDesignAsync(_designPath);
        await SeedRuntimeAsync(_runtimePath);
        await SeedDesignAsync(_sharedPath);
        await SeedRuntimeAsync(_sharedPath);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _designPath, _runtimePath, _sharedPath })
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                if (File.Exists(path + suffix))
                    File.Delete(path + suffix);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Split_targets_produce_the_same_counts_as_one_shared_target()
    {
        var shared = new GroundworkWorkflowPortfolioDataSource(
            () => new SqliteConnection($"Data Source={_sharedPath}"),
            GroundworkRunHealthDialect.Sqlite,
            PayloadSerializer());

        var split = new GroundworkWorkflowPortfolioDataSource(
            () => new SqliteConnection($"Data Source={_designPath}"),
            GroundworkRunHealthDialect.Sqlite,
            PayloadSerializer(),
            () => new SqliteConnection($"Data Source={_runtimePath}"));

        var sharedCounts = await shared.QueryBaseCountsAsync(TenantId, AsOf);
        var splitCounts = await split.QueryBaseCountsAsync(TenantId, AsOf);

        Assert.Equal(3, sharedCounts.ActiveDefinitionCount);
        Assert.Equal(2, sharedCounts.PublishedDefinitionCount);
        Assert.Equal(sharedCounts.ActiveDefinitionCount, splitCounts.ActiveDefinitionCount);
        Assert.Equal(sharedCounts.PublishedDefinitionCount, splitCounts.PublishedDefinitionCount);
        Assert.Equal(sharedCounts.UnpublishedDraftCount, splitCounts.UnpublishedDraftCount);
    }

    [Fact]
    public async Task A_design_target_with_no_definitions_never_queries_the_runtime_target()
    {
        var emptyDesign = Path.Combine(Path.GetTempPath(), $"portfolio-empty-{Guid.NewGuid():N}.db");
        await CreateDocumentsTableAsync(emptyDesign);
        try
        {
            var split = new GroundworkWorkflowPortfolioDataSource(
                () => new SqliteConnection($"Data Source={emptyDesign}"),
                GroundworkRunHealthDialect.Sqlite,
                PayloadSerializer(),
                () => throw new InvalidOperationException("The runtime target must not be queried."));

            var counts = await split.QueryBaseCountsAsync(TenantId, AsOf);

            Assert.Equal(0, counts.ActiveDefinitionCount);
            Assert.Equal(0, counts.PublishedDefinitionCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(emptyDesign))
                File.Delete(emptyDesign);
        }
    }

    private static IPayloadSerializer PayloadSerializer() =>
        new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private static async Task CreateDocumentsTableAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS groundwork_documents (
                document_kind TEXT NOT NULL,
                id TEXT NOT NULL,
                content_json TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedDesignAsync(string path)
    {
        await CreateDocumentsTableAsync(path);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        foreach (var (kind, id, json) in DesignRows())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO groundwork_documents (document_kind, id, content_json) VALUES ($k, $i, $c);";
            command.Parameters.AddWithValue("$k", kind);
            command.Parameters.AddWithValue("$i", id);
            command.Parameters.AddWithValue("$c", json);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRuntimeAsync(string path)
    {
        await CreateDocumentsTableAsync(path);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        foreach (var (kind, id, json) in RuntimeRows())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO groundwork_documents (document_kind, id, content_json) VALUES ($k, $i, $c);";
            command.Parameters.AddWithValue("$k", kind);
            command.Parameters.AddWithValue("$i", id);
            command.Parameters.AddWithValue("$c", json);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<(string Kind, string Id, string Json)> DesignRows()
    {
        const string definitionKind = "workflowDefinition";
        const string draftKind = "workflowDefinitionDraft";
        foreach (var definitionId in new[] { "def-1", "def-2", "def-3" })
        {
            yield return (definitionKind, definitionId, DefinitionJson(definitionId));
        }

        yield return (draftKind, "draft-1", DraftJson("draft-1", "def-1"));
    }

    private static string DefinitionJson(string definitionId) =>
        "{\"entity\":{\"id\":\"" + definitionId + "\",\"tenantId\":\"" + TenantId + "\",\"deletedAt\":null}}";

    private static string DraftJson(string draftId, string definitionId) =>
        "{\"entity\":{\"id\":\"" + draftId + "\",\"tenantId\":\"" + TenantId +
        "\",\"workflowDefinitionId\":\"" + definitionId +
        "\",\"lastModifiedAt\":\"2026-08-01T00:00:00+00:00\",\"createdAt\":\"2026-08-01T00:00:00+00:00\"}}";

    private static string ReferenceJson(
        string definitionId,
        WorkflowExecutableReferenceScope scope,
        string? expiresAt) =>
        "{\"reference\":{\"definitionId\":\"" + definitionId + "\",\"scope\":" + (int)scope +
        ",\"deletedAt\":null,\"expiresAt\":" + (expiresAt is null ? "null" : "\"" + expiresAt + "\"") + "}}";

    private static IEnumerable<(string Kind, string Id, string Json)> RuntimeRows()
    {
        const string referenceKind = "workflowExecutableSourceReference";
        // def-1 and def-2 are published; def-3 is not. An expired reference must not count.
        yield return (referenceKind, "ref-1",
            ReferenceJson("def-1", WorkflowExecutableReferenceScope.Published, expiresAt: null));
        yield return (referenceKind, "ref-2",
            ReferenceJson("def-2", WorkflowExecutableReferenceScope.Published, expiresAt: null));
        yield return (referenceKind, "ref-3",
            ReferenceJson("def-3", WorkflowExecutableReferenceScope.Published, expiresAt: "2026-01-01T00:00:00+00:00"));
    }
}
