using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Data.Sqlite;
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
    private static readonly DateTimeOffset Seeded = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
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
        await OpenAsync(emptyDesign, WorkflowsDesignStorageManifest.Create());
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

    /// <summary>
    /// Opens the database on Groundwork's physical surface with the given manifest, exactly as a
    /// production host does, so the seeded rows land in the tables the data source reads.
    /// </summary>
    private static async Task<IDocumentStore> OpenAsync(string path, StorageManifest manifest) =>
        await SqliteDocumentStoreFactory.OpenPhysicalAsync(
            $"Data Source={path}",
            manifest,
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            DocumentStoreAccess.Scoped(new StorageScope(TenantId)),
            options: new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

    private static async Task SeedDesignAsync(string path)
    {
        var store = await OpenAsync(path, WorkflowsDesignStorageManifest.Create());
        foreach (var definitionId in new[] { "def-1", "def-2", "def-3" })
        {
            var definition = new WorkflowDefinition
            {
                Id = definitionId, TenantId = TenantId, Name = definitionId,
                CreatedAt = Seeded, LastModifiedAt = Seeded
            };
            await store.SaveAsync(new(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition.Id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(
                    new DefinitionDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition),
                    GroundworkDesignJson.Options)));
        }

        var payloadSerializer = PayloadSerializer();
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1", WorkflowDefinitionId = "def-1", TenantId = TenantId,
            State = WorkflowDefinitionState.Empty,
            StateSource = payloadSerializer.Serialize(WorkflowDefinitionState.Empty),
            CreatedAt = Seeded, LastModifiedAt = Seeded
        };
        await store.SaveAsync(new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new DraftDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft, []),
                GroundworkDesignDocumentSerialization.Create(payloadSerializer))));
    }

    private static async Task SeedRuntimeAsync(string path)
    {
        var store = await OpenAsync(path, ElsaRuntimeStorageManifest.CreatePhysicalized());
        var referenceStore = new GroundworkWorkflowExecutableSourceReferenceStore(
            store, new GroundworkRuntimeDocumentSerializer());
        // def-1 and def-2 are published; def-3's reference is expired before AsOf and must not count.
        await referenceStore.SaveAsync(Reference("ref-1", "def-1", expiresAt: null));
        await referenceStore.SaveAsync(Reference("ref-2", "def-2", expiresAt: null));
        await referenceStore.SaveAsync(Reference("ref-3", "def-3", expiresAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    private static WorkflowExecutableSourceReference Reference(string id, string definitionId, DateTimeOffset? expiresAt) =>
        new(id, $"artifact-{id}", "WorkflowDefinitionVersion", $"version-{id}", "1",
            definitionId, $"version-{id}", "1", Seeded, Seeded, WorkflowExecutableReferenceScope.Published, expiresAt);

    private sealed record DefinitionDocument(string Collection, WorkflowDefinition Entity);

    private sealed record DraftDocument(
        string Collection,
        WorkflowDefinitionDraft Entity,
        IReadOnlyCollection<DesignMetadataRecord> Layout);
}
