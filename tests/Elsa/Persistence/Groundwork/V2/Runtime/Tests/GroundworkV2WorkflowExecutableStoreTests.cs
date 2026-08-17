using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowExecutableStoreTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_immutable_coordination_and_guarded_delete(string providerName)
    {
        var connectionString = providerName == "sqlite"
            ? null
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        RequireOrSkip(providerName != "sqlite" && string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} proof.");
        await using var fixture = NativeFixture.Create(providerName, connectionString);
        RequireOrSkip(!fixture.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
            $"The {providerName} provider does not advertise atomic commit.");
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable($"artifact-{providerName}", "1"));
        await store.SaveAsync(Executable($"artifact-{providerName}", "2"));
        await store.SaveAsync(Executable($"artifact-{providerName}-next", "1"));
        Assert.Equal("1", (await store.FindAsync($"artifact-{providerName}"))!.Identity.ArtifactVersion);
        Assert.Null(await fixture.Store("tenant-b").FindAsync($"artifact-{providerName}"));
        var firstPage = await store.ListPageAsync(new RuntimeStorePageRequest(1));
        Assert.Equal($"artifact-{providerName}", Assert.Single(firstPage.Items).Identity.ArtifactId);
        Assert.NotNull(firstPage.NextContinuationToken);
        var secondPage = await store.ListPageAsync(new RuntimeStorePageRequest(1, firstPage.NextContinuationToken));
        Assert.Equal($"artifact-{providerName}-next", Assert.Single(secondPage.Items).Identity.ArtifactId);
        var lease = await store.TryAcquireRootWriteLeaseAsync(
            $"artifact-{providerName}", "writer", now.AddMinutes(1), now);
        Assert.NotNull(lease);
        Assert.Equal(lease, await store.TryAcquireRootWriteLeaseAsync(
            $"artifact-{providerName}", "writer", now.AddMinutes(5), now));
        Assert.False(await store.RenewRootWriteLeaseAsync(
            lease! with { ConcurrencyToken = "stale" }, now.AddMinutes(2), now));
        Assert.True(await store.RenewRootWriteLeaseAsync(lease!, now.AddMinutes(2), now));
        Assert.Null(await store.TryBeginDeletionAsync(
            $"artifact-{providerName}", "delete", now.AddMinutes(1), now));
        await store.ReleaseRootWriteLeaseAsync(lease! with { ConcurrencyToken = "stale" });
        Assert.Null(await store.TryBeginDeletionAsync(
            $"artifact-{providerName}", "delete", now.AddMinutes(1), now));
        await store.ReleaseRootWriteLeaseAsync(lease!);
        var guard = await store.TryBeginDeletionAsync(
            $"artifact-{providerName}", "delete", now.AddMinutes(1), now);
        Assert.NotNull(guard);
        Assert.Null(await store.TryAcquireRootWriteLeaseAsync(
            $"artifact-{providerName}", "writer-2", now.AddMinutes(1), now));
        Assert.False(await store.DeleteAsync(guard! with { ConcurrencyToken = "stale" }, now));
        Assert.True(await store.DeleteAsync(guard!, now));
        Assert.Null(await store.FindAsync($"artifact-{providerName}"));
        Assert.True(await store.DeleteAsync($"artifact-{providerName}-next"));
        Assert.Equal(BatchWriteOptions.Exact, fixture.LastUnitOfWorkOptions);
    }

    [Fact]
    public async Task Sqlite_save_is_immutable_and_creates_coordination_atomically()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");

        await store.SaveAsync(Executable("artifact-1", "1"));
        await store.SaveAsync(Executable("artifact-1", "2"));

        var found = await store.FindAsync("artifact-1");
        Assert.NotNull(found);
        Assert.Equal("1", found!.Identity.ArtifactVersion);
        Assert.Equal("child", Assert.Single(Assert.Single(found.RootActivity.ChildSlots).Activities).ExecutableNodeId);
        Assert.Equal("return input.customerEmail;", found.NodesById["child"].InputBindings["text"].Expression!.Expression);
        Assert.Equal(BatchWriteOptions.Exact, fixture.LastUnitOfWorkOptions);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind
            ],
            fixture.LastUnitOfWorkUnitIds);
        Assert.True(fixture.RowExists(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
            "artifact-1"));
    }

    [Fact]
    public async Task Sqlite_lease_and_deletion_guard_transitions_are_fenced()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Executable("artifact-lease", "1"));
        var executableVersion = fixture.RowVersion(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
            "artifact-lease");

        var lease = await store.TryAcquireRootWriteLeaseAsync(
            "artifact-lease", "writer-a", now.AddMinutes(1), now);
        Assert.NotNull(lease);
        var duplicate = await store.TryAcquireRootWriteLeaseAsync(
            "artifact-lease", "writer-a", now.AddMinutes(5), now);
        Assert.Equal(lease, duplicate);
        Assert.Null(await store.TryBeginDeletionAsync(
            "artifact-lease", "delete-a", now.AddMinutes(2), now));
        Assert.False(await store.RenewRootWriteLeaseAsync(
            lease! with { ConcurrencyToken = "stale" }, now.AddMinutes(2), now));
        Assert.True(await store.RenewRootWriteLeaseAsync(lease!, now.AddMinutes(2), now));
        await store.ReleaseRootWriteLeaseAsync(lease! with { ConcurrencyToken = "stale" });
        Assert.Null(await store.TryBeginDeletionAsync(
            "artifact-lease", "delete-a", now.AddMinutes(2), now));
        await store.ReleaseRootWriteLeaseAsync(lease!);

        var guard = await store.TryBeginDeletionAsync(
            "artifact-lease", "delete-a", now.AddMinutes(2), now);
        Assert.NotNull(guard);
        Assert.Equal(guard, await store.TryBeginDeletionAsync(
            "artifact-lease", "delete-a", now.AddMinutes(5), now));
        Assert.Null(await store.TryAcquireRootWriteLeaseAsync(
            "artifact-lease", "writer-b", now.AddMinutes(1), now));
        Assert.False(await store.CancelDeletionAsync(guard! with { ConcurrencyToken = "stale" }));
        Assert.True(await store.CancelDeletionAsync(guard!));
        Assert.NotNull(await store.TryAcquireRootWriteLeaseAsync(
            "artifact-lease", "writer-b", now.AddMinutes(1), now));
        Assert.Equal(
            executableVersion,
            fixture.RowVersion(
                "tenant-a",
                ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                "artifact-lease"));
    }

    [Fact]
    public async Task Duplicate_owners_do_not_extend_expiry_and_missing_artifacts_never_authorize()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Null(await store.TryAcquireRootWriteLeaseAsync(
            "missing", "writer", now.AddMinutes(1), now));
        Assert.Null(await store.TryBeginDeletionAsync(
            "missing", "delete", now.AddMinutes(1), now));
        fixture.InsertOrphanCoordination("tenant-a", "orphan");
        Assert.Null(await store.TryAcquireRootWriteLeaseAsync(
            "orphan", "writer", now.AddMinutes(1), now));
        Assert.Null(await store.TryBeginDeletionAsync(
            "orphan", "delete", now.AddMinutes(1), now));

        await store.SaveAsync(Executable("artifact-expiry", "1"));
        var lease = await store.TryAcquireRootWriteLeaseAsync(
            "artifact-expiry", "writer", now.AddMinutes(1), now);
        Assert.Equal(lease, await store.TryAcquireRootWriteLeaseAsync(
            "artifact-expiry", "writer", now.AddMinutes(5), now));
        var guard = await store.TryBeginDeletionAsync(
            "artifact-expiry", "delete", now.AddMinutes(3), now.AddMinutes(2));
        Assert.NotNull(guard);
        var replacement = await store.TryBeginDeletionAsync(
            "artifact-expiry", "delete", now.AddMinutes(5), now.AddMinutes(4));
        Assert.NotNull(replacement);
        Assert.NotEqual(guard!.ConcurrencyToken, replacement!.ConcurrencyToken);
    }

    [Fact]
    public async Task Sqlite_guarded_and_privileged_deletes_remove_both_rows_atomically()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Executable("artifact-guarded", "1"));
        var guard = await store.TryBeginDeletionAsync(
            "artifact-guarded", "delete-a", now.AddMinutes(1), now);
        Assert.NotNull(guard);
        Assert.False(await store.DeleteAsync(guard! with { ConcurrencyToken = "stale" }, now));
        Assert.True(await store.DeleteAsync(guard!, now));
        Assert.Null(await store.FindAsync("artifact-guarded"));
        Assert.False(fixture.RowExists(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
            "artifact-guarded"));

        await store.SaveAsync(Executable("artifact-admin", "1"));
        Assert.True(await store.DeleteAsync("artifact-admin"));
        Assert.False(await store.DeleteAsync("artifact-admin"));
        Assert.False(fixture.RowExists(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
            "artifact-admin"));
    }

    [Fact]
    public async Task Sqlite_listing_skips_poison_and_advances_provider_keyset_pages()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        await store.SaveAsync(Executable("artifact-1", "1"));
        await store.SaveAsync(Executable("artifact-3", "1"));
        fixture.InsertPoison("tenant-a", "artifact-2");

        var first = await store.ListPageAsync(new RuntimeStorePageRequest(1));
        Assert.Equal("artifact-1", Assert.Single(first.Items).Identity.ArtifactId);
        Assert.NotNull(first.NextContinuationToken);
        var second = await store.ListPageAsync(new RuntimeStorePageRequest(1, first.NextContinuationToken));
        Assert.Equal("artifact-3", Assert.Single(second.Items).Identity.ArtifactId);

        var content = fixture.Content("tenant-a", "artifact-1");
        using var document = JsonDocument.Parse(content);
        Assert.False(document.RootElement.TryGetProperty("nodes", out _));
        Assert.False(document.RootElement.TryGetProperty("nodesById", out _));
    }

    [Fact]
    public async Task Scope_boundaries_fail_closed_before_provider_io()
    {
        await using var fixture = Fixture.Create();
        await fixture.Store("tenant-a").SaveAsync(Executable("artifact-scope", "1"));
        Assert.Null(await fixture.Store("tenant-b").FindAsync("artifact-scope"));

        fixture.ResetOpenCount();
        var global = fixture.Store(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            global.FindAsync("artifact-scope").AsTask());
        Assert.Equal(0, fixture.OpenCount);
    }

    [Fact]
    public async Task Sqlite_concurrent_create_and_lease_guard_races_converge()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        await Task.WhenAll(Enumerable.Range(1, 12)
            .Select(version => store.SaveAsync(Executable("artifact-race", version.ToString())).AsTask()));
        Assert.NotNull(await store.FindAsync("artifact-race"));

        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var artifactId = $"artifact-transition-{attempt:D2}";
            await store.SaveAsync(Executable(artifactId, "1"));
            var leaseTask = store.TryAcquireRootWriteLeaseAsync(
                artifactId, "writer", now.AddMinutes(1), now).AsTask();
            var guardTask = store.TryBeginDeletionAsync(
                artifactId, "delete", now.AddMinutes(1), now).AsTask();
            await Task.WhenAll(leaseTask, guardTask);
            var lease = await leaseTask;
            var guard = await guardTask;
            Assert.NotEqual(lease is null, guard is null);
            if (lease is not null)
                await store.ReleaseRootWriteLeaseAsync(lease);
            if (guard is not null)
                Assert.True(await store.CancelDeletionAsync(guard));
        }
    }

    [Fact]
    public async Task Lease_and_guard_race_from_the_same_coordination_revision_has_exactly_one_winner()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        await store.SaveAsync(Executable("artifact-barrier", "1"));
        store = fixture.BarrierStore("tenant-a");
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        var leaseTask = Task.Run(async () => await store.TryAcquireRootWriteLeaseAsync(
            "artifact-barrier", "writer", now.AddMinutes(1), now));
        var guardTask = Task.Run(async () => await store.TryBeginDeletionAsync(
            "artifact-barrier", "delete", now.AddMinutes(1), now));
        await Task.WhenAll(leaseTask, guardTask);

        Assert.NotEqual((await leaseTask) is null, (await guardTask) is null);
    }

    [Fact]
    public async Task Poison_paging_refuses_a_repeated_provider_continuation()
    {
        await using var fixture = Fixture.Create();
        var store = fixture.RepeatingContinuationStore("tenant-a");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ListPageAsync(new RuntimeStorePageRequest(1)).AsTask());

        Assert.Contains("repeated continuation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_exact_delete_between_presence_reads_returns_no_authority()
    {
        await using var fixture = Fixture.Create();
        var deletingStore = fixture.Store("tenant-a");
        await deletingStore.SaveAsync(Executable("artifact-delete-race", "1"));
        var source = new DeleteBetweenPresenceReadsSource(fixture.Source)
        {
            BeforeExecutableRead = () => fixture.DeleteAtomically("tenant-a", "artifact-delete-race")
        };
        IWorkflowExecutableStore store = new GroundworkV2WorkflowExecutableStore(
            source,
            new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(await store.TryAcquireRootWriteLeaseAsync(
            "artifact-delete-race", "writer", now.AddMinutes(1), now));
        Assert.Null(await deletingStore.FindAsync("artifact-delete-race"));
    }

    [Fact]
    public async Task Exact_create_failure_rolls_back_artifact_and_coordination_together()
    {
        await using var fixture = Fixture.Create();
        IWorkflowExecutableStore store = fixture.Store("tenant-a");
        fixture.FailNextCommitWithOutcomes();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Executable("artifact-rollback", "1")).AsTask());
        Assert.Null(await store.FindAsync("artifact-rollback"));
        Assert.False(fixture.RowExists(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
            "artifact-rollback"));

        await store.SaveAsync(Executable("artifact-rollback", "1"));
        Assert.NotNull(await store.FindAsync("artifact-rollback"));
    }

    [Fact]
    public async Task Sqlite_artifact_and_fencing_state_survive_provider_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-v2-executable-restart-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        WorkflowExecutableRootWriteLease lease;
        try
        {
            using (var connection = new SqliteProviderFactory().Create(connectionString))
            {
                connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind));
                connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind));
                IWorkflowExecutableStore store = new GroundworkV2WorkflowExecutableStore(
                    new Source(connection),
                    new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
                await store.SaveAsync(Executable("artifact-restart", "1"));
                lease = (await store.TryAcquireRootWriteLeaseAsync(
                    "artifact-restart", "writer", now.AddMinutes(1), now))!;
            }

            using (var connection = new SqliteProviderFactory().Create(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkV2WorkflowExecutableStore(
                    new Source(connection),
                    new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
                Assert.Equal("artifact-restart", (await store.FindAsync("artifact-restart"))!.Identity.ArtifactId);
                Assert.Equal(lease, await store.TryAcquireRootWriteLeaseAsync(
                    "artifact-restart", "writer", now.AddMinutes(5), now));
                await store.ReleaseRootWriteLeaseAsync(lease);
                Assert.NotNull(await store.TryBeginDeletionAsync(
                    "artifact-restart", "delete", now.AddMinutes(1), now));
            }
        }
        finally
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}-journal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
        }
    }

    [Fact]
    public void Current_rows_refuse_schema_identity_and_projection_drift()
    {
        var values = GroundworkV2WorkflowExecutableStorageConventions.Values(
            Executable("artifact-validation", "1")).Values;
        var wrongSchema = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2WorkflowExecutableStorageConventions.Deserialize(wrongSchema));

        var wrongIdentity = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.IdField] = "artifact-forged"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2WorkflowExecutableStorageConventions.Deserialize(wrongIdentity));

        var wrongProjection = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField] = "artifact-forged"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2WorkflowExecutableStorageConventions.Deserialize(wrongProjection));

        var malformedCoordination = GroundworkV2WorkflowExecutableStorageConventions.CoordinationValues(
            "artifact-validation",
            new GroundworkV2WorkflowExecutableStorageConventions.CoordinationState(
                new Dictionary<string, GroundworkV2WorkflowExecutableStorageConventions.RootWriteLeaseState>
                {
                    ["writer"] = new("different-writer", "", DateTimeOffset.UnixEpoch)
                },
                null)).Values;
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2WorkflowExecutableStorageConventions.DeserializeCoordination(malformedCoordination));
    }

    private static WorkflowExecutable Executable(string artifactId, string artifactVersion)
    {
        var expression = new RuntimeExpressionBinding(
            language: "JavaScript",
            expression: "return input.customerEmail;",
            resultType: new RuntimeValueTypeDescriptor("String", null, null),
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["customerEmail"] = new WorkflowRequestExpressionParameterBinding("customerEmail")
            },
            options: Json("""{ "strict": true }"""));
        var child = new ExecutableNode(
            "child",
            "authored-child",
            "Elsa.SendEmail",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "Elsa.Activities.SendEmailDescriptor",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                Json("""{ "kind": "Send" }""")),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["text"] = new(
                    "text",
                    new ValueTypeDescriptor("String"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: expression)
            },
            new Dictionary<string, string>());
        var root = new ExecutableNode(
            "root",
            "authored-root",
            "Elsa.Sequence",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "Elsa.Activities.SequenceDescriptor",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                Json("""{ "kind": "Sequence" }""")),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [child])]);
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity(
                artifactId,
                "definition-1",
                "version-1",
                artifactVersion,
                $"hash-{artifactId}"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            null,
            null,
            null,
            null,
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path;
        private readonly IStorageProviderConnection connection;
        private readonly Source source;

        private Fixture(string path, IStorageProviderConnection connection)
        {
            this.path = path;
            this.connection = connection;
            source = new Source(connection);
            connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind));
            connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind));
        }

        public BatchWriteOptions? LastUnitOfWorkOptions => source.LastUnitOfWorkOptions;

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds => source.LastUnitOfWorkUnitIds;

        public int OpenCount => source.OpenCount;

        public Source Source => source;

        public static Fixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-v2-executable-{Guid.NewGuid():N}.db");
            return new Fixture(path, new SqliteProviderFactory().Create($"Data Source={path}"));
        }

        public GroundworkV2WorkflowExecutableStore Store(string tenant) =>
            new(source, new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

        public GroundworkV2WorkflowExecutableStore Store(PersistenceAccessContext access) =>
            new(source, new Accessor(access));

        public GroundworkV2WorkflowExecutableStore BarrierStore(string tenant) =>
            new(new BarrierSource(source), new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

        public GroundworkV2WorkflowExecutableStore RepeatingContinuationStore(string tenant) =>
            new(new RepeatingContinuationSource(source), new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

        public void ResetOpenCount() => source.ResetOpenCount();

        public void FailNextCommitWithOutcomes() => source.FailNextCommitWithOutcomes = true;

        public bool RowExists(string tenant, string unitId, string id) =>
            source.Open(unitId, StorageAccess.Scoped(new StorageScope(tenant)))
                .Read(GroundworkRuntimeRowStore.Key(id)) is not null;

        public long? RowVersion(string tenant, string unitId, string id) =>
            source.Open(unitId, StorageAccess.Scoped(new StorageScope(tenant)))
                .Read(GroundworkRuntimeRowStore.Key(id))?.Version;

        public string Content(string tenant, string id)
        {
            var row = source.Open(
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                    StorageAccess.Scoped(new StorageScope(tenant)))
                .Read(GroundworkRuntimeRowStore.Key(id));
            return row!.Values.Values[ElsaRuntimeV2StorageManifest.ContentField] switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                var value => throw new Xunit.Sdk.XunitException($"Unexpected content value '{value?.GetType()}'.")
            };
        }

        public void InsertPoison(string tenant, string id)
        {
            var values = GroundworkRuntimeRowStore.Values(
                id,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                """{ "identity": "poison" }""",
                new Dictionary<string, object?>
                {
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                    [ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField] = id
                });
            var result = source.Open(
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                    StorageAccess.Scoped(new StorageScope(tenant)))
                .Insert(values, WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, result.Status);
        }

        public void InsertOrphanCoordination(string tenant, string id)
        {
            var result = source.Open(
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
                    StorageAccess.Scoped(new StorageScope(tenant)))
                .Insert(
                    GroundworkV2WorkflowExecutableStorageConventions.EmptyCoordinationValues(id),
                    WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, result.Status);
        }

        public bool DeleteAtomically(string tenant, string id)
        {
            using var unitOfWork = source.BeginUnitOfWork(
                StorageAccess.Scoped(new StorageScope(tenant)),
                BatchWriteOptions.Exact,
                [
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind
                ]);
            var key = GroundworkRuntimeRowStore.Key(id);
            var executableUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind);
            var coordinationUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind);
            var executable = unitOfWork.OpenSession(executableUnit).Read(key)!;
            var coordination = unitOfWork.OpenSession(coordinationUnit).Read(key)!;
            unitOfWork.Stage(RowWrite.Delete(executableUnit, key, WriteOptions.IfVersion(executable.Version!.Value)));
            unitOfWork.Stage(RowWrite.Delete(coordinationUnit, key, WriteOptions.IfVersion(coordination.Version!.Value)));
            return unitOfWork.CommitWithOutcomes().IsSuccessful;
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}-journal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        private readonly string? sqlitePath;
        private readonly IStorageProviderConnection connection;
        private readonly Source source;

        private NativeFixture(
            string? sqlitePath,
            IStorageProviderConnection connection,
            IReadOnlyDictionary<string, StorageUnit> units)
        {
            this.sqlitePath = sqlitePath;
            this.connection = connection;
            source = new Source(connection, units);
        }

        public IReadOnlyList<CapabilityDescriptor> Capabilities => connection.Capabilities;

        public BatchWriteOptions? LastUnitOfWorkOptions => source.LastUnitOfWorkOptions;

        public static NativeFixture Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-v2-executable-{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = providerName switch
            {
                "sqlite" => new SqliteProviderFactory().Create(connectionString!),
                "postgresql" => new PostgreSqlProviderFactory().Create(connectionString!),
                "sqlserver" => new SqlServerProviderFactory().Create(connectionString!),
                "mongodb" => new MongoProviderFactory().Create(connectionString!),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
            var logicalUnits = new[]
            {
                ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind),
                ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind)
            };
            var units = logicalUnits.ToDictionary(
                unit => unit.Id.Value,
                unit => providerName == "sqlite"
                    ? unit
                    : unit with
                    {
                        Id = new StorageUnitId($"{unit.Id.Value}-{Guid.NewGuid():N}"[..42]),
                        Name = $"{unit.Name}_{Guid.NewGuid():N}"[..52]
                    },
                StringComparer.Ordinal);
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);
            return new NativeFixture(sqlitePath, connection, units);
        }

        public GroundworkV2WorkflowExecutableStore Store(string tenant) =>
            new(source, new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            if (sqlitePath is not null)
                foreach (var candidate in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal", $"{sqlitePath}-journal" })
                    if (File.Exists(candidate))
                        File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }

    private class Source :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        private readonly IStorageProviderConnection connection;
        private readonly IReadOnlyDictionary<string, StorageUnit> units;

        public Source(
            IStorageProviderConnection connection,
            IReadOnlyDictionary<string, StorageUnit>? units = null)
        {
            this.connection = connection;
            this.units = units ?? new[]
            {
                ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind),
                ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind)
            }.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        }

        public int OpenCount { get; private set; }

        public bool FailNextCommitWithOutcomes { get; set; }

        public BatchWriteOptions? LastUnitOfWorkOptions { get; private set; }

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return connection.OpenSession(Resolve(unitId), access);
        }

        public void ResetOpenCount() => OpenCount = 0;

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            LastUnitOfWorkOptions = options;
            LastUnitOfWorkUnitIds = unitIds.ToArray();
            var inner = connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(Resolve).ToArray());
            if (!FailNextCommitWithOutcomes)
                return inner;
            FailNextCommitWithOutcomes = false;
            return new FailedOutcomeUnitOfWork(inner);
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        private StorageUnit Resolve(string unitId) =>
            units.TryGetValue(unitId, out var logical)
                ? logical
                : units.Values.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
    }

    private sealed class FailedOutcomeUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        private readonly List<RowWrite> staged = [];

        public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);

        public void Stage(RowWrite write)
        {
            staged.Add(write);
            inner.Stage(write);
        }

        public BatchWriteSummary Commit() => FailureReport().Summary;

        public BatchWriteReport CommitWithOutcomes() => FailureReport();

        public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(FailureReport().Summary);

        public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(FailureReport());

        public void Rollback() => inner.Rollback();

        public void Dispose() => inner.Dispose();

        private BatchWriteReport FailureReport()
        {
            if (staged.Count != 2)
                throw new InvalidOperationException("The executable exact failure requires two staged rows.");
            return new BatchWriteReport(staged.Select((write, index) => new RowWriteOutcome(
                write,
                new WriteOutcome(
                    index == 0 ? WriteOutcomeStatus.Inserted : WriteOutcomeStatus.ConcurrencyConflict,
                    index == 0 ? 1 : null))).ToArray());
        }
    }

    private sealed class BarrierSource(Source inner) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        private readonly Barrier barrier = new(2);
        private int readsRemaining = 2;

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => inner.Capabilities(targetName);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var session = inner.Open(unitId, access, targetName);
            return StringComparer.Ordinal.Equals(
                unitId,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind)
                ? new BarrierSession(session, barrier, () => Interlocked.Decrement(ref readsRemaining) >= 0)
                : session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => inner.BeginUnitOfWork(access, options, unitIds, targetName);

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class RepeatingContinuationSource(Source inner) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => inner.Capabilities(targetName);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var session = inner.Open(unitId, access, targetName);
            return StringComparer.Ordinal.Equals(unitId, ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind)
                ? new RepeatingContinuationSession(session)
                : session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => inner.BeginUnitOfWork(access, options, unitIds, targetName);

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class DeleteBetweenPresenceReadsSource(Source inner) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public Func<bool>? BeforeExecutableRead { get; set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => inner.Capabilities(targetName);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var session = inner.Open(unitId, access, targetName);
            if (!StringComparer.Ordinal.Equals(unitId, ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind))
                return session;
            return new BeforeReadSession(session, () =>
            {
                var callback = BeforeExecutableRead;
                BeforeExecutableRead = null;
                _ = callback?.Invoke();
            });
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => inner.BeginUnitOfWork(access, options, unitIds, targetName);

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class BarrierSession(
        IStorageSession inner,
        Barrier barrier,
        Func<bool> shouldWait) : IStorageSession, IConcurrencyStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;

        public StoredEntry? Read(StorageKey key)
        {
            var row = inner.Read(key);
            if (shouldWait() && !barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The coordination race did not reach both readers.");
            return row;
        }

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => inner.Query(request, options);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);
    }

    private sealed class RepeatingContinuationSession(IStorageSession inner) : IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            var poison = GroundworkRuntimeRowStore.Values(
                "artifact-poison",
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                """{ "identity": "poison" }""",
                new Dictionary<string, object?>
                {
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                    [ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField] = "artifact-poison"
                });
            return new QueryMaterializedResult([poison.Values], null, "repeat");
        }

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    }

    private sealed class BeforeReadSession(IStorageSession inner, Action beforeRead) : IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;

        public StoredEntry? Read(StorageKey key)
        {
            beforeRead();
            return inner.Read(key);
        }

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => inner.Query(request, options);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

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
}
