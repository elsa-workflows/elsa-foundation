using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

[Collection(GroundworkV2NativeProviderMatrixCollection.Name)]
public sealed class GroundworkV2WorkflowExecutableSourceReferenceStoreTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [Fact]
    public void The_v2_store_implements_the_public_source_reference_contract()
    {
        Assert.Contains(
            typeof(IWorkflowExecutableSourceReferenceStore),
            typeof(GroundworkV2WorkflowExecutableSourceReferenceStore).GetInterfaces());
    }

    [Fact]
    public async Task Sqlite_round_trips_identity_pages_live_filters_and_gc_primitives()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkV2WorkflowExecutableSourceReferenceStore(source, accessor);
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var live = Reference("ref-live", "artifact-a", WorkflowExecutableReferenceScope.Published);
        var expired = Reference("ref-expired", "artifact-a", WorkflowExecutableReferenceScope.TestRun) with
        {
            ExpiresAt = now.AddMinutes(-1)
        };
        var retired = Reference("ref-retired", "artifact-b", WorkflowExecutableReferenceScope.Published)
            .Retire(now.AddMinutes(-2), "replaced");

        await store.SaveAsync(live);
        await store.SaveAsync(expired);
        await store.SaveAsync(retired);

        var rawLive = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Read(GroundworkRuntimeRowStore.Key(live.SourceReferenceId));
        Assert.NotNull(rawLive);
        Assert.Equal(DateTimeOffset.MaxValue, rawLive!.Values.Values[ElsaRuntimeV2StorageManifest.ExpiresAtField]);
        Assert.Equal(false, rawLive.Values.Values[ElsaRuntimeV2StorageManifest.IsRetiredField]);

        var found = await store.FindAsync(live.SourceReferenceId);
        Assert.NotNull(found);
        Assert.Equal(live.SourceReferenceId, found!.SourceReferenceId);
        Assert.Equal(live.ArtifactId, found.ArtifactId);
        Assert.Equal(live.Layout, found.Layout);
        Assert.Equal(live.AuthoredInputs.Select(input => input.InputKey), found.AuthoredInputs.Select(input => input.InputKey));
        Assert.Equal(live.ActivityPresentation, found.ActivityPresentation);
        Assert.Equal(live.TenantId, found.TenantId);
        var artifactFirst = await store.ListByArtifactPageAsync(
            new WorkflowExecutableSourceReferenceArtifactPageQuery("artifact-a", limit: 1));
        Assert.Equal(["ref-expired"], artifactFirst.Items.Select(reference => reference.SourceReferenceId));
        Assert.NotNull(artifactFirst.NextContinuationToken);
        var artifactSecond = await store.ListByArtifactPageAsync(
            new WorkflowExecutableSourceReferenceArtifactPageQuery(
                "artifact-a", limit: 1, artifactFirst.NextContinuationToken));
        Assert.Equal(["ref-live"], artifactSecond.Items.Select(reference => reference.SourceReferenceId));
        Assert.Null(artifactSecond.NextContinuationToken);
        Assert.Equal(
            ["ref-live"],
            (await store.ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(
                liveOnly: true,
                now: now))).Items.Select(reference => reference.SourceReferenceId));
        Assert.Equal(
            ["artifact-b", "artifact-c"],
            await store.ListUnreferencedArtifactIdsAsync(
                new WorkflowExecutableArtifactCandidateBatch(["artifact-a", "artifact-b", "artifact-c"]), now));

        Assert.Equal(
            ["ref-expired", "ref-retired"],
            await store.DeleteExpiredOrRetiredAsync(new WorkflowExecutableSourceReferenceCleanupBatch(2), now));
        Assert.Null(await store.FindAsync(expired.SourceReferenceId));
        Assert.Null(await store.FindAsync(retired.SourceReferenceId));
        Assert.NotNull(await store.FindAsync(live.SourceReferenceId));
    }

    [Fact]
    public async Task Scoped_access_is_fail_closed_and_projection_content_mismatch_is_rejected()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var reference = Reference("ref-boundary", "artifact-boundary", WorkflowExecutableReferenceScope.TestRun);
        var scoped = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await scoped.SaveAsync(reference);
        var otherScope = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));
        Assert.Null(await otherScope.FindAsync(reference.SourceReferenceId));

        var global = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Global));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            global.FindAsync(reference.SourceReferenceId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scoped.SaveAsync(Reference(
                "ref-wrong-tenant",
                "artifact-boundary",
                WorkflowExecutableReferenceScope.TestRun) with
            { TenantId = "tenant-b" }).AsTask());
        Assert.Null(await scoped.FindAsync("ref-wrong-tenant"));

        var raw = source.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope("tenant-a")));
        var entry = raw.Read(GroundworkRuntimeRowStore.Key(reference.SourceReferenceId));
        Assert.NotNull(entry);
        var values = entry!.Values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.ArtifactIdField] = "artifact-tampered";
        Assert.True(raw.Update(new StorageValues(values), WriteOptions.Unconditional).Succeeded);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scoped.FindAsync(reference.SourceReferenceId).AsTask());
    }

    [Fact]
    public async Task Create_only_save_refuses_replays_and_retirement_preserves_current_facts()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        connection.Schema.Apply(unit);
        var store = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            new DirectSessionSource(connection, unit),
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var original = Reference("ref-update", "artifact-before", WorkflowExecutableReferenceScope.TestRun);
        var replacement = original with { ArtifactId = "artifact-after", SourceVersion = "2" };
        await store.SaveAsync(original);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(original).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(replacement).AsTask());
        Assert.Equal("artifact-before", (await store.FindAsync(original.SourceReferenceId))!.ArtifactId);

        var deletedAt = new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero);
        Assert.True(await store.RetireAsync(original.SourceReferenceId, deletedAt, "done"));
        Assert.True(await store.RetireAsync(original.SourceReferenceId, deletedAt.AddHours(1), "ignored"));
        var retired = await store.FindAsync(original.SourceReferenceId);
        Assert.Equal(deletedAt, retired!.DeletedAt);
        Assert.Equal("done", retired.DeletedReason);
        Assert.False(retired.IsLive(deletedAt.AddHours(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(retired with { DeletedAt = null, DeletedReason = null }).AsTask());
        var stillRetired = await store.FindAsync(original.SourceReferenceId);
        Assert.Equal(deletedAt, stillRetired!.DeletedAt);
        Assert.Equal("done", stillRetired.DeletedReason);
    }

    [Fact]
    public async Task Try_restore_requires_the_expected_retired_snapshot_and_restores_live_reference()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var store = new GroundworkV2WorkflowExecutableSourceReferenceStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var original = Reference("ref-restore", "artifact-before", WorkflowExecutableReferenceScope.TestRun);
        await store.SaveAsync(original);

        var retiredAt = new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero);
        Assert.True(await store.RetireAsync(original.SourceReferenceId, retiredAt, "activation-replaced"));
        var retired = await store.FindAsync(original.SourceReferenceId);
        Assert.NotNull(retired);
        Assert.True(await store.TryRestoreAsync(retired!, original));
        var restored = await store.FindAsync(original.SourceReferenceId);
        Assert.Equal(original.ArtifactId, restored!.ArtifactId);
        Assert.Null(restored.DeletedAt);

        Assert.True(await store.RetireAsync(original.SourceReferenceId, retiredAt.AddMinutes(1), "activation-replaced"));
        var expected = await store.FindAsync(original.SourceReferenceId);
        var raw = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")));
        var changed = expected! with
        {
            Layout = [new WorkflowExecutableLayoutRecord("node-1", 42, 43, 120, 80, JsonSerializer.SerializeToElement(new { changed = true }))],
            LayoutSidecar = new ExecutableLayoutSidecar([
                new ExecutableLayoutBoundarySegment(
                    "boundary-1",
                    new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.TemplateBoundary, "boundary-1")]),
                    "template-v2",
                    [new ExecutableActivityLayoutRecord(
                        "template-node",
                        "authored-node-1",
                        "node-1",
                        42,
                        43,
                        ActivityType: "test/activity",
                        ActivityTypeVersion: "2.0.0",
                        HasPinnedGeometry: false)],
                    [new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.NestedPlacement, "nested-1")])])
            ]),
            AuthoredInputs = [new WorkflowExecutableAuthoredInputRecord(
                "node-1",
                "input",
                "json",
                JsonSerializer.SerializeToElement(new { value = 1 }))],
            ActivityPresentation = [new WorkflowExecutableActivityPresentationRecord("node-1", "Changed display", "Changed description")]
        };
        Assert.True(raw.Update(
                GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Values(changed),
                WriteOptions.Unconditional)
            .Succeeded);

        Assert.False(await store.TryRestoreAsync(expected!, original));
        var stillRetired = await store.FindAsync(original.SourceReferenceId);
        Assert.Equal(expected.DeletedReason, stillRetired!.DeletedReason);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameIdentity(changed, stillRetired));
        Assert.NotNull(stillRetired.DeletedAt);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_matrix_round_trips_source_reference(string providerName)
    {
        var sqlitePath = providerName == "sqlite"
            ? Path.Combine(Path.GetTempPath(), $"elsa-runtime-source-reference-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = providerName == "sqlite"
            ? $"Data Source={sqlitePath}"
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        RequireOrSkip(string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} provider proof.");

        try
        {
            using var connection = providerName switch
            {
                "sqlite" => new SqliteProviderFactory().Create(connectionString!),
                "postgresql" => new PostgreSqlProviderFactory().Create(connectionString!),
                "sqlserver" => new SqlServerProviderFactory().Create(connectionString!),
                "mongodb" => new MongoProviderFactory().Create(connectionString!),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
            var declared = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
            var unit = providerName == "sqlite"
                ? declared
                : declared with
                {
                    Id = new StorageUnitId($"gwv2_source_reference_{Guid.NewGuid():N}"),
                    Name = $"gwv2_source_reference_{Guid.NewGuid():N}"
                };
            unit = unit with { Name = unit.Id.Value };
            connection.Schema.Apply(unit);
            var store = new GroundworkV2WorkflowExecutableSourceReferenceStore(
                new DirectSessionSource(connection, unit),
                new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("matrix-scope"))));
            var reference = Reference($"matrix-{providerName}", $"artifact-{providerName}", WorkflowExecutableReferenceScope.TestRun) with
            {
                TenantId = "matrix-scope"
            };
            var live = Reference($"matrix-live-{providerName}", $"artifact-live-{providerName}", WorkflowExecutableReferenceScope.Published) with
            {
                TenantId = "matrix-scope"
            };
            await store.SaveAsync(reference);
            await store.SaveAsync(live);
            Assert.Equal(reference.SourceReferenceId, (await store.FindAsync(reference.SourceReferenceId))!.SourceReferenceId);
            Assert.Equal(
                [live.SourceReferenceId],
                (await store.ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(
                    liveOnly: true,
                    now: new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero))))
                .Items.Select(item => item.SourceReferenceId));
            Assert.Null(await new GroundworkV2WorkflowExecutableSourceReferenceStore(
                new DirectSessionSource(connection, unit),
                new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("matrix-other"))))
                .FindAsync(reference.SourceReferenceId));
            Assert.Equal(
                [reference.SourceReferenceId],
                await store.DeleteExpiredOrRetiredAsync(
                    new WorkflowExecutableSourceReferenceCleanupBatch(1),
                    new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero)));
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var file in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(file))
                        File.Delete(file);
            }
        }
    }

    private static WorkflowExecutableSourceReference Reference(
        string id,
        string artifactId,
        WorkflowExecutableReferenceScope scope) =>
        new(
            id,
            artifactId,
            "published",
            $"source-{id}",
            "1",
            "definition-1",
            "definition-version-1",
            "artifact-version-1",
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero),
            scope == WorkflowExecutableReferenceScope.Published
                ? new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero)
                : null,
            scope,
            scope == WorkflowExecutableReferenceScope.TestRun
                ? new DateTimeOffset(2026, 8, 17, 11, 0, 0, TimeSpan.Zero)
                : null,
            Layout: [new WorkflowExecutableLayoutRecord("node-1", 1, 2, 3, 4)],
            LayoutSidecar: new ExecutableLayoutSidecar([]),
            AuthoredInputs: [new WorkflowExecutableAuthoredInputRecord(
                "node-1", "Input", "literal", System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement)],
            TenantId: "tenant-a",
            ActivityPresentation: [new WorkflowExecutableActivityPresentationRecord("node-1", "Node")]);

    private static void RequireOrSkip(bool unavailable, string message)
    {
        if (!unavailable)
            return;
        if (StringComparer.Ordinal.Equals(
                Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX"),
                "1"))
        {
            throw new InvalidOperationException($"Required Groundwork v2 native-provider evidence is unavailable: {message}");
        }

        Skip.If(true, message);
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public StorageUnit Unit(string documentKind, string? targetName = null) => unit;

        public IStorageSession Open(
            string documentKind,
            StorageAccess access,
            string? targetName = null) => connection.OpenSession(unit, access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());
    }

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly string path;

        private NativeProviderRuntime(string path) => this.path = path;

        public static NativeProviderRuntime Create() =>
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-source-reference-{Guid.NewGuid():N}.db"));

        public IStorageProviderConnection OpenConnection() =>
            new SqliteProviderFactory().Create($"Data Source={path}");

        public ValueTask DisposeAsync()
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            return ValueTask.CompletedTask;
        }
    }
}
