using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.Tests;

/// <summary>
/// The portfolio tile spans two v2 lanes and the query model has no joins, so the counts are three ordered
/// walks correlated on definition id. These tests hold that correlation to the behaviour the shared-table
/// v1 source had: soft-deleted definitions do not count, only the current draft of each live definition
/// counts, and a publication counts only while its source reference is published, unretired and unexpired.
/// </summary>
public sealed class GroundworkV2WorkflowPortfolioDataSourceTests : IAsyncDisposable
{
    private const string Tenant = "tenant-a";
    private static readonly DateTimeOffset AsOf = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakePayloadSerializer PayloadSerializer = new();

    private readonly string designPath = TempPath("design");
    private readonly string runtimePath = TempPath("runtime");
    private readonly IStorageProviderConnection design;
    private readonly IStorageProviderConnection runtime;
    private readonly LaneSessionSource shared;
    private readonly LaneSessionSource split;

    public GroundworkV2WorkflowPortfolioDataSourceTests()
    {
        design = Connect(designPath);
        runtime = Connect(runtimePath);
        foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
            design.Schema.Apply(unit);
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
        {
            design.Schema.Apply(unit);
            runtime.Schema.Apply(unit);
        }

        shared = new(design, design);
        split = new(design, runtime);
    }

    [Fact]
    public void Explicit_v2_registration_replaces_the_portfolio_source_and_claims_both_lanes()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowPortfolioDataSource, InMemoryWorkflowPortfolioDataSource>();
        services.AddSingleton<IGroundworkStorageSessionSource>(shared);
        services.AddSingleton<IPayloadSerializer>(PayloadSerializer);
        services.AddSingleton<IPersistenceAccessContextAccessor>(Accessor());

        services.AddGroundworkV2WorkflowPortfolio("design", "runtime");

        using var provider = services.BuildServiceProvider();
        Assert.IsType<GroundworkV2WorkflowPortfolioDataSource>(
            provider.GetRequiredService<IWorkflowPortfolioDataSource>());
        var registrations = provider.GetRequiredService<GroundworkStorageUnitRegistry>().Registrations;
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind &&
            registration.TargetName == "design");
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind &&
            registration.TargetName == "design");
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind &&
            registration.TargetName == "runtime");
    }

    [Fact]
    public async Task Counts_exclude_deleted_definitions_dead_references_and_superseded_drafts()
    {
        SeedDefinition("def-live-published");
        SeedDefinition("def-live-published-expired-and-current");
        SeedDefinition("def-live-unpublished");
        SeedDefinition("def-deleted", deletedAt: AsOf.AddDays(-1));
        SeedDraft("draft-a", "def-live-published", AsOf.AddDays(-2));
        SeedDraft("draft-b-old", "def-live-unpublished", AsOf.AddDays(-3));
        SeedDraft("draft-b-new", "def-live-unpublished", AsOf.AddDays(-1));
        SeedDraft("draft-of-deleted", "def-deleted", AsOf.AddDays(-1));

        await SeedReferenceAsync("ref-live", "def-live-published");
        await SeedReferenceAsync("ref-expired", "def-live-published-expired-and-current", expiresAt: AsOf.AddDays(-1));
        await SeedReferenceAsync("ref-current", "def-live-published-expired-and-current", expiresAt: AsOf.AddDays(1));
        await SeedReferenceAsync("ref-retired", "def-live-unpublished", deletedAt: AsOf.AddDays(-1));
        await SeedReferenceAsync("ref-test-run", "def-live-unpublished", scope: WorkflowExecutableReferenceScope.TestRun);
        await SeedReferenceAsync("ref-orphaned", "def-deleted");

        var counts = await Source(shared).QueryBaseCountsAsync(Tenant, AsOf);

        // Three live definitions; two of them published; three of them carry a current draft.
        Assert.Equal(new WorkflowPortfolioBaseCounts(3, 2, 2), counts);
    }

    [Fact]
    public async Task Split_targets_produce_the_same_counts_as_one_shared_target()
    {
        SeedDefinition("def-1");
        SeedDefinition("def-2");
        SeedDefinition("def-3");
        SeedDraft("draft-1", "def-1", AsOf.AddDays(-1));
        foreach (var lane in new[] { design, runtime })
        {
            await SeedReferenceAsync("ref-1", "def-1", lane: lane);
            await SeedReferenceAsync("ref-2", "def-2", lane: lane);
            await SeedReferenceAsync("ref-3", "def-3", expiresAt: AsOf.AddDays(-1), lane: lane);
        }

        var sharedCounts = await Source(shared).QueryBaseCountsAsync(Tenant, AsOf);
        var splitCounts = await Source(split).QueryBaseCountsAsync(Tenant, AsOf);

        Assert.Equal(new WorkflowPortfolioBaseCounts(3, 2, 1), sharedCounts);
        Assert.Equal(sharedCounts, splitCounts);
    }

    [Fact]
    public async Task A_design_lane_with_no_definitions_never_opens_the_runtime_lane()
    {
        var counts = await Source(split).QueryBaseCountsAsync(Tenant, AsOf);

        Assert.Equal(new WorkflowPortfolioBaseCounts(0, 0, 0), counts);
        Assert.Equal(0, split.RuntimeOpenCount);
    }

    [Fact]
    public async Task The_stream_yields_the_current_draft_of_every_live_definition()
    {
        SeedDefinition("def-1");
        SeedDefinition("def-2");
        SeedDefinition("def-deleted", deletedAt: AsOf.AddDays(-1));
        SeedDraft("draft-1-old", "def-1", AsOf.AddDays(-3));
        SeedDraft("draft-1-current", "def-1", AsOf.AddDays(-1));
        SeedDraft("draft-2", "def-2", AsOf.AddDays(-2));
        SeedDraft("draft-deleted", "def-deleted", AsOf);

        var drafts = new List<WorkflowDefinitionDraft>();
        await foreach (var draft in Source(shared).StreamCurrentDraftsAsync(Tenant))
            drafts.Add(draft);

        Assert.Equal(["draft-1-current", "draft-2"], drafts.Select(draft => draft.Id).Order(StringComparer.Ordinal));
        Assert.All(drafts, draft => Assert.NotNull(draft.State));
    }

    [Fact]
    public async Task A_catalog_larger_than_one_page_is_counted_without_scope_leakage()
    {
        for (var index = 0; index < 300; index++)
        {
            SeedDefinition($"def-{index:D4}");
            if (index % 3 == 0)
                SeedDraft($"draft-{index:D4}", $"def-{index:D4}", AsOf.AddMinutes(-index));
            if (index % 2 == 0)
                await SeedReferenceAsync($"ref-{index:D4}", $"def-{index:D4}");
        }

        SeedDefinition("def-other-tenant", tenant: "tenant-b");
        await SeedReferenceAsync("ref-other-tenant", "def-other-tenant", tenant: "tenant-b");

        var counts = await Source(shared).QueryBaseCountsAsync(Tenant, AsOf);

        Assert.Equal(new WorkflowPortfolioBaseCounts(300, 150, 100), counts);
    }

    [Fact]
    public async Task Access_without_one_explicit_scope_is_refused()
    {
        var source = new GroundworkV2WorkflowPortfolioDataSource(
            shared,
            new FixedAccessContextAccessor(PersistenceAccessContext.Global),
            PayloadSerializer);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source.QueryBaseCountsAsync(Tenant, AsOf));
    }

    private GroundworkV2WorkflowPortfolioDataSource Source(LaneSessionSource sessions) =>
        new(sessions, Accessor(), PayloadSerializer);

    private static IPersistenceAccessContextAccessor Accessor(string tenant = Tenant) =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

    private void SeedDefinition(string definitionId, DateTimeOffset? deletedAt = null, string tenant = Tenant)
    {
        var definition = new WorkflowDefinition
        {
            Id = definitionId,
            TenantId = tenant,
            Name = definitionId,
            CreatedAt = AsOf.AddDays(-10),
            LastModifiedAt = AsOf.AddDays(-10),
            DeletedAt = deletedAt
        };
        Write(
            design,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition,
                GroundworkDesignJson.Options,
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection),
            tenant);
    }

    private void SeedDraft(string draftId, string definitionId, DateTimeOffset lastModifiedAt, string tenant = Tenant)
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = definitionId,
            TenantId = tenant,
            State = WorkflowDefinitionState.Empty,
            StateSource = PayloadSerializer.Serialize(WorkflowDefinitionState.Empty),
            CreatedAt = lastModifiedAt,
            LastModifiedAt = lastModifiedAt
        };
        Write(
            design,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                draft,
                GroundworkDesignDocumentSerialization.Create(PayloadSerializer),
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
                []),
            tenant);
    }

    private async Task SeedReferenceAsync(
        string sourceReferenceId,
        string definitionId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? deletedAt = null,
        WorkflowExecutableReferenceScope scope = WorkflowExecutableReferenceScope.Published,
        string tenant = Tenant,
        IStorageProviderConnection? lane = null)
    {
        var reference = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            $"artifact-{sourceReferenceId}",
            "WorkflowDefinitionVersion",
            $"{definitionId}-source",
            "1",
            definitionId,
            $"{definitionId}-v1",
            "1",
            AsOf.AddDays(-5),
            AsOf.AddDays(-5),
            scope,
            expiresAt,
            deletedAt)
        {
            TenantId = tenant
        };
        // Defaults to the design database, which is also the shared host's single lane.
        var store = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            new LaneSessionSource(design, lane ?? design),
            Accessor(tenant));
        await store.SaveAsync(reference);
    }

    private static void Write(
        IStorageProviderConnection connection,
        string unitId,
        StorageValues values,
        string tenant)
    {
        var unit = WorkflowsDesignStorageManifest.Require(unitId);
        var session = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(tenant)));
        var outcome = session.Upsert(values, WriteOptions.Unconditional);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
    }

    private static IStorageProviderConnection Connect(string path) =>
        new SqliteProviderFactory().Create($"Data Source={path}");

    private static string TempPath(string lane) =>
        Path.Combine(Path.GetTempPath(), $"elsa-v2-portfolio-{lane}-{Guid.NewGuid():N}.db");

    public ValueTask DisposeAsync()
    {
        design.Dispose();
        runtime.Dispose();
        foreach (var root in new[] { designPath, runtimePath })
            foreach (var path in new[] { root, $"{root}-shm", $"{root}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        return ValueTask.CompletedTask;
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    /// <summary>Routes each unit to the lane that owns it, so one instance models both a shared and a split host.</summary>
    private sealed class LaneSessionSource(IStorageProviderConnection design, IStorageProviderConnection runtime)
        : IGroundworkStorageSessionSource
    {
        public int RuntimeOpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var unit = Unit(unitId, targetName);
            if (!IsRuntime(unitId))
                return design.OpenSession(unit, access);
            RuntimeOpenCount++;
            return runtime.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            IsRuntime(unitId)
                ? ElsaRuntimeV2StorageManifest.Require(unitId)
                : WorkflowsDesignStorageManifest.Require(unitId);

        private static bool IsRuntime(string unitId) =>
            ElsaRuntimeV2StorageManifest.CreateUnits()
                .Any(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
    }

    private sealed class FakePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }
}
