using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
public sealed class GroundworkV2WorkflowTestScopeStoreTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_scope_lifecycle_and_exact_cleanup_uow(string providerName)
    {
        var connectionString = providerName == "sqlite"
            ? null
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        RequireOrSkip(providerName != "sqlite" && string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} proof.");
        await using var runtime = NativeFixture.Create(providerName, connectionString);
        RequireOrSkip(!runtime.Connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
            $"The {providerName} provider does not advertise atomic commit.");

        var scope = Scope($"scope-{providerName}", "tenant-a", CreatedAt.AddHours(1));
        var scopes = runtime.ScopeStore("tenant-a");
        var dispatches = runtime.DispatchStore("tenant-a");
        var cleanup = runtime.CleanupStore("tenant-a");
        await scopes.CreateAsync(scope, CreatedAt);
        var pending = Dispatch($"pending-{providerName}", scope, WorkflowDispatchStatus.Pending);
        await dispatches.SaveAsync(pending);
        var started = Dispatch(
            $"started-{providerName}",
            scope,
            WorkflowDispatchStatus.Pending,
            createdAt: CreatedAt.AddSeconds(1));
        await dispatches.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, CreatedAt.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(1)));
        var intent = CancellationIntent(started, CreatedAt.AddMinutes(1));
        var result = await cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            2,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(1, result.CancellationQueued);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(
            (await dispatches.FindAsync(started.DispatchId))!));
        var identity = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId);
        Assert.True(runtime.OutboxExists(
            "tenant-a",
            identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}")));
        Assert.Equal(BatchWriteOptions.Exact, runtime.LastUnitOfWorkOptions);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind
            ],
            runtime.LastUnitOfWorkLogicalIds);
    }

    [Fact]
    public async Task Sqlite_scope_lifecycle_replays_conflict_pages_and_admission_cas()
    {
        await using var fixture = Fixture.Create();
        var scopeStore = fixture.ScopeStore("tenant-a");
        var scope = Scope("scope-a", "tenant-a", CreatedAt.AddMinutes(10));

        var created = await scopeStore.CreateAsync(scope, CreatedAt);
        Assert.Equal(created, await scopeStore.CreateAsync(scope, CreatedAt.AddSeconds(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scopeStore.CreateAsync(Scope("scope-a", "tenant-a", CreatedAt.AddMinutes(11)), CreatedAt).AsTask());

        await scopeStore.AssertOpenAsync(scope, CreatedAt.AddMinutes(1));
        var expired = Scope("scope-expired", "tenant-a", CreatedAt.AddMinutes(1));
        await scopeStore.CreateAsync(expired, CreatedAt);
        var closing = await scopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(2)));
        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, closing.Disposition);
        Assert.Equal(WorkflowTestScopeState.Closing, (await scopeStore.FindAsync(scope.ScopeId))!.State);

        var page = await scopeStore.QueryAsync(new WorkflowTestScopePageQuery(
            CreatedAt.AddMinutes(2), 1, ContinuationToken: null));
        Assert.Single(page.Items);
        Assert.NotNull(page.ContinuationToken);
        var next = await scopeStore.QueryAsync(new WorkflowTestScopePageQuery(
            CreatedAt.AddMinutes(2), 1, ContinuationToken: page.ContinuationToken));
        Assert.Single(next.Items);

        var closed = await scopeStore.CompleteAsync(scope.ScopeId, CreatedAt.AddMinutes(3));
        Assert.Equal(WorkflowTestScopeState.Closed, closed.State);
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() =>
            scopeStore.AssertOpenAsync(scope, CreatedAt.AddMinutes(3)).AsTask());
    }

    [Fact]
    public async Task Scope_and_partition_boundaries_fail_closed_and_oversized_pages_do_not_open_a_session()
    {
        await using var fixture = Fixture.Create();
        var tenantA = fixture.ScopeStore("tenant-a");
        var scope = Scope("scope-boundary", "tenant-a", CreatedAt.AddMinutes(10));
        await tenantA.CreateAsync(scope, CreatedAt);

        Assert.Null(await fixture.ScopeStore("tenant-b").FindAsync(scope.ScopeId));

        fixture.ResetOpenCount();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ScopeStore("tenant-b").CreateAsync(scope, CreatedAt).AsTask());
        Assert.Equal(0, fixture.OpenCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantA.CreateAsync(
                Scope(scope.ScopeId, "tenant-a", scope.ExpiresAt, "partition-b"),
                CreatedAt).AsTask());

        fixture.ResetOpenCount();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            tenantA.QueryAsync(new WorkflowTestScopePageQuery(
                CreatedAt,
                GroundworkV2WorkflowTestScopeStore.MaximumPageSize + 1)).AsTask());
        Assert.Equal(0, fixture.OpenCount);

        var malformedToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"{CreatedAt:O}\u001fdispatch-a\u001fdispatch-b"));
        fixture.ResetOpenCount();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CleanupStore("tenant-a").CleanupAsync(
            scope,
            CreatedAt,
            1,
            new Dictionary<string, RuntimePostCommitIntent>(),
            malformedToken).AsTask());
        Assert.Equal(0, fixture.OpenCount);
    }

    [Fact]
    public async Task Cleanup_continuation_is_bound_to_the_exact_scope_before_provider_io()
    {
        await using var fixture = Fixture.Create();
        var scopes = fixture.ScopeStore("tenant-a");
        var dispatches = fixture.DispatchStore("tenant-a");
        var cleanup = fixture.CleanupStore("tenant-a");
        var sourceScope = Scope("scope-token-source", "tenant-a", CreatedAt.AddHours(1));
        var targetScope = Scope("scope-token-target", "tenant-a", CreatedAt.AddHours(1));
        await scopes.CreateAsync(sourceScope, CreatedAt);
        await scopes.CreateAsync(targetScope, CreatedAt);
        await dispatches.SaveAsync(Dispatch("source-first", sourceScope, WorkflowDispatchStatus.Pending));
        await dispatches.SaveAsync(Dispatch(
            "source-second",
            sourceScope,
            WorkflowDispatchStatus.Pending,
            createdAt: CreatedAt.AddSeconds(1)));
        await dispatches.SaveAsync(Dispatch("target-first", targetScope, WorkflowDispatchStatus.Pending));
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            sourceScope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(1)));
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            targetScope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(1)));

        var sourcePage = await cleanup.CleanupAsync(
            sourceScope,
            CreatedAt.AddMinutes(1),
            1,
            new Dictionary<string, RuntimePostCommitIntent>());
        Assert.NotNull(sourcePage.ContinuationToken);

        fixture.ResetOpenCount();
        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.CleanupAsync(
            targetScope,
            CreatedAt.AddMinutes(1),
            1,
            new Dictionary<string, RuntimePostCommitIntent>(),
            sourcePage.ContinuationToken).AsTask());
        Assert.Equal(0, fixture.OpenCount);
    }

    [Fact]
    public async Task Cleanup_rolls_back_without_started_child_responsibility_then_replays_one_outbox()
    {
        await using var fixture = Fixture.Create();
        var scope = Scope("scope-rollback", "tenant-a", CreatedAt.AddHours(1));
        var scopes = fixture.ScopeStore("tenant-a");
        var dispatches = fixture.DispatchStore("tenant-a");
        var cleanup = fixture.CleanupStore("tenant-a");
        await scopes.CreateAsync(scope, CreatedAt);
        var pending = Dispatch("rollback-pending", scope, WorkflowDispatchStatus.Pending);
        var started = Dispatch(
            "rollback-started",
            scope,
            WorkflowDispatchStatus.Pending,
            createdAt: CreatedAt.AddSeconds(1));
        await dispatches.SaveAsync(pending);
        await dispatches.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, CreatedAt.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            2,
            new Dictionary<string, RuntimePostCommitIntent>()).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatches.FindAsync(started.DispatchId))!.Status);

        var identity = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId);
        var outboxId = identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        Assert.False(fixture.OutboxExists("tenant-a", outboxId));

        var intent = CancellationIntent(started, CreatedAt.AddMinutes(1));
        fixture.FailNextCommitWithOutcomes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            2,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent }).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatches.FindAsync(started.DispatchId))!.Status);
        Assert.False(fixture.OutboxExists("tenant-a", outboxId));
        Assert.Equal(BatchWriteOptions.Exact, fixture.LastUnitOfWorkOptions);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind
            ],
            fixture.LastUnitOfWorkUnitIds);

        var committed = await cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            2,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });
        Assert.Equal(1, committed.CancelledBeforeAdmission);
        Assert.Equal(1, committed.CancellationQueued);
        Assert.True(fixture.OutboxExists("tenant-a", outboxId));

        var replay = await cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            2,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });
        Assert.Equal(0, replay.Inspected);
        Assert.True(fixture.OutboxExists("tenant-a", outboxId));

        await scopes.CompleteAsync(scope.ScopeId, CreatedAt.AddMinutes(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(2),
            2,
            new Dictionary<string, RuntimePostCommitIntent>()).AsTask());
    }

    [Fact]
    public async Task Sqlite_cleanup_is_atomic_fire_and_forget_only_and_continues_after_one_hundred()
    {
        await using var fixture = Fixture.Create();
        var scope = Scope("scope-cleanup", "tenant-a", CreatedAt.AddHours(1));
        var scopeStore = fixture.ScopeStore("tenant-a");
        var dispatchStore = fixture.DispatchStore("tenant-a");
        var cleanup = fixture.CleanupStore("tenant-a");
        await scopeStore.CreateAsync(scope, CreatedAt);

        var pending = Enumerable.Range(0, 101)
            .Select(index => Dispatch($"pending-{index:D3}", scope, WorkflowDispatchStatus.Pending))
            .ToArray();
        foreach (var record in pending)
            await dispatchStore.SaveAsync(record);
        var waited = Dispatch("waited", scope, WorkflowDispatchStatus.Pending, WorkflowDispatchMode.WaitForCompletion);
        await dispatchStore.SaveAsync(waited);
        var started = Dispatch("started", scope, WorkflowDispatchStatus.Pending);
        await dispatchStore.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, CreatedAt.AddMinutes(2));
        await dispatchStore.SaveAsync(started);
        await scopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(1)));

        var intent = CancellationIntent(started, CreatedAt.AddMinutes(1));
        var first = await cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });
        Assert.Equal(100, first.Inspected);
        Assert.NotNull(first.ContinuationToken);
        Assert.InRange(first.CancellationQueued, 0, 1);
        Assert.Equal(100 - first.CancellationQueued, first.CancelledBeforeAdmission);

        var second = await cleanup.CleanupAsync(
            scope,
            CreatedAt.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent },
            first.ContinuationToken);
        Assert.Equal(2, second.Inspected);
        Assert.Equal(1, first.CancellationQueued + second.CancellationQueued);
        Assert.Equal(101, first.CancelledBeforeAdmission + second.CancelledBeforeAdmission);
        Assert.True(second.RemainingLive >= 1); // started cancellation remains live until its outbox is delivered.
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(waited.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await dispatchStore.FindAsync(pending[0].DispatchId))!.Status);
    }

    private static WorkflowTestScope Scope(
        string id,
        string tenant,
        DateTimeOffset expiresAt,
        string partition = WorkflowExecutionPartition.DefaultValue) =>
        new(id, expiresAt, tenant, new WorkflowExecutionPartition(partition));

    private static WorkflowDispatchRecord Dispatch(
        string id,
        WorkflowTestScope scope,
        WorkflowDispatchStatus status,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget,
        DateTimeOffset? createdAt = null)
    {
        var recordedAt = createdAt ?? CreatedAt;
        var parent = $"parent-{id}";
        var activity = $"activity-{id}";
        var identity = new WorkflowDispatchIdentity(parent, activity);
        var record = new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{id}", "definition-child", "version-child", "1", $"hash-{id}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{id}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            scope.TenantId,
            scope.Partition,
            WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [],
            recordedAt,
            recordedAt,
            new Dictionary<string, string>(),
            scope);
        return status == WorkflowDispatchStatus.Started
            ? record.TransitionTo(WorkflowDispatchStatus.Started, recordedAt.AddTicks(1))
            : record;
    }

    private static RuntimePostCommitIntent CancellationIntent(
        WorkflowDispatchRecord started,
        DateTimeOffset requestedAt)
    {
        var identity = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            started.ParentWorkflowExecutionId,
            "Elsa.Activities.DispatchWorkflow.CancelChild",
            requestedAt,
            started.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = started.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = started.ChildWorkflowExecutionId
            });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path;
        private readonly IStorageProviderConnection connection;
        private readonly DirectSource source;

        private Fixture(string path, IStorageProviderConnection connection)
        {
            this.path = path;
            this.connection = connection;
            source = new DirectSource(connection);
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits().Where(unit =>
                         unit.Id.Value is ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind or
                         ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind or
                         ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind))
                connection.Schema.Apply(unit);
        }

        public static Fixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-v2-scope-{Guid.NewGuid():N}.db");
            return new Fixture(path, new SqliteProviderFactory().Create($"Data Source={path}"));
        }

        public GroundworkV2WorkflowTestScopeStore ScopeStore(string tenant) =>
            new(source, Access(tenant));

        public GroundworkV2WorkflowDispatchStore DispatchStore(string tenant) =>
            new(source, Access(tenant));

        public GroundworkV2WorkflowTestScopeCleanupStore CleanupStore(string tenant) =>
            new(source, Access(tenant));

        public int OpenCount => source.OpenCount;

        public void ResetOpenCount() => source.ResetOpenCount();

        public BatchWriteOptions? LastUnitOfWorkOptions => source.LastUnitOfWorkOptions;

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds => source.LastUnitOfWorkUnitIds;

        public void FailNextCommitWithOutcomes() => source.FailNextCommitWithOutcomes = true;

        public bool OutboxExists(string tenant, string outboxId) =>
            source.Open(
                    ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                    StorageAccess.Scoped(new StorageScope(tenant)))
                .Read(GroundworkRuntimeRowStore.Key(outboxId)) is not null;

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        private readonly string? sqlitePath;
        private readonly IStorageProviderConnection connection;
        private readonly NativeSource source;

        private NativeFixture(
            string? sqlitePath,
            IStorageProviderConnection connection,
            IReadOnlyDictionary<string, StorageUnit> units)
        {
            this.sqlitePath = sqlitePath;
            this.connection = connection;
            source = new NativeSource(connection, units);
        }

        public IStorageProviderConnection Connection => connection;

        public static NativeFixture Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-v2-scope-{Guid.NewGuid():N}.db");
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
            var units = ElsaRuntimeV2StorageManifest.CreateUnits()
                .Where(unit => unit.Id.Value is
                    ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind or
                    ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind or
                    ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind)
                .ToDictionary(
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

        public GroundworkV2WorkflowTestScopeStore ScopeStore(string tenant) => new(source, Access(tenant));

        public GroundworkV2WorkflowDispatchStore DispatchStore(string tenant) => new(source, Access(tenant));

        public GroundworkV2WorkflowTestScopeCleanupStore CleanupStore(string tenant) => new(source, Access(tenant));

        public BatchWriteOptions? LastUnitOfWorkOptions => source.LastUnitOfWorkOptions;

        public IReadOnlyList<string>? LastUnitOfWorkLogicalIds => source.LastUnitOfWorkLogicalIds;

        public bool OutboxExists(string tenant, string outboxId) =>
            source.Open(
                    ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                    StorageAccess.Scoped(new StorageScope(tenant)))
                .Read(GroundworkRuntimeRowStore.Key(outboxId)) is not null;

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            if (sqlitePath is not null)
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal", $"{sqlitePath}-journal" })
                    if (File.Exists(path))
                        File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NativeSource(
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public BatchWriteOptions? LastUnitOfWorkOptions { get; private set; }

        public IReadOnlyList<string>? LastUnitOfWorkLogicalIds { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(Resolve(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            LastUnitOfWorkOptions = options;
            LastUnitOfWorkLogicalIds = unitIds
                .Select(id => units.Single(pair => StringComparer.Ordinal.Equals(pair.Value.Id.Value, id)).Key)
                .ToArray();
            return connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        private StorageUnit Resolve(string unitId) =>
            units.TryGetValue(unitId, out var logical)
                ? logical
                : units.Values.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
    }

    private sealed class DirectSource(IStorageProviderConnection connection) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public int OpenCount { get; private set; }

        public BatchWriteOptions? LastUnitOfWorkOptions { get; private set; }

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds { get; private set; }

        public bool FailNextCommitWithOutcomes { get; set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);
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
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());
            if (!FailNextCommitWithOutcomes)
                return inner;
            FailNextCommitWithOutcomes = false;
            return new FailedOutcomeUnitOfWork(inner);
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
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

        public BatchWriteSummary Commit() => CommitWithOutcomes().Summary;

        public BatchWriteReport CommitWithOutcomes() => FailureReport();

        public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(FailureReport().Summary);

        public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(FailureReport());

        public void Rollback() => inner.Rollback();

        public void Dispose() => inner.Dispose();

        private BatchWriteReport FailureReport()
        {
            if (staged.Count == 0)
                throw new InvalidOperationException("The simulated exact outcome failure requires staged rows.");
            return new BatchWriteReport(staged.Select((write, index) => new RowWriteOutcome(
                write,
                new WriteOutcome(
                    index == staged.Count - 1
                        ? WriteOutcomeStatus.ConcurrencyConflict
                        : WriteOutcomeStatus.Upserted,
                    index == staged.Count - 1 ? null : 1))).ToArray());
        }
    }

    private static TestAccessContextAccessor Access(string tenant) =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

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
}
