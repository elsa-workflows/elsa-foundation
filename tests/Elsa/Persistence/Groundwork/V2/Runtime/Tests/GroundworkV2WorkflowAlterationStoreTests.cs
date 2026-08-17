using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.SqlServer;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

[Collection(GroundworkV2NativeProviderMatrixCollection.Name)]
public sealed class GroundworkV2WorkflowAlterationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [Fact]
    public async Task Sqlite_admission_capture_seal_page_claim_terminal_and_reconcile_round_trip()
    {
        await using var fixture = Fixture.Create();
        var store = fixture.Store("tenant-a");

        var admitted = await store.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"));
        var replay = await store.AdmitAsync(Plan("different-id", "tenant-a", "key-a", "canonical-a"));
        Assert.False(admitted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal("plan-a", replay.Plan.PlanId);

        var captured = await store.CaptureAsync(
            admitted.Plan.PlanId,
            admitted.Plan.Revision,
            [
                new WorkflowAlterationCapturedTarget("execution-b", "tenant-a"),
                new WorkflowAlterationCapturedTarget("execution-a", "tenant-a"),
                new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")
            ],
            null);
        var sealedPlan = await store.SealAsync(captured.PlanId, captured.Revision, Now);
        var page = await store.PageJobsAsync(sealedPlan.PlanId, 1);
        var secondPage = await store.PageJobsAsync(sealedPlan.PlanId, 1, page.NextCursor);
        var claim = await store.ClaimNextAsync(sealedPlan.PlanId, "worker-a", Now, TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now);
        await store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            claim.JobId, claim.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now));
        var counts = await store.GetJobCountsAsync(sealedPlan.PlanId);
        var reconciledBeforeSecond = await store.ReconcileAsync(sealedPlan.PlanId, Now);

        Assert.Equal(WorkflowAlterationPlanStatus.Queued, sealedPlan.Status);
        Assert.Equal(["execution-a"], page.Items.Select(job => job.WorkflowExecutionId));
        Assert.True(page.HasNext);
        Assert.Equal(["execution-b"], secondPage.Items.Select(job => job.WorkflowExecutionId));
        Assert.False(secondPage.HasNext);
        Assert.Equal(WorkflowAlterationJobStatus.Running, claim.Status);
        Assert.Equal(1, counts.Succeeded);
        Assert.Equal(1, counts.Pending);
        Assert.Equal(WorkflowAlterationPlanStatus.Running, reconciledBeforeSecond.Status);

        var secondClaim = await store.ClaimNextAsync(sealedPlan.PlanId, "worker-b", Now, TimeSpan.FromMinutes(1));
        Assert.NotNull(secondClaim);
        await store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            secondClaim.JobId, secondClaim.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-b", Now));
        var completed = await store.ReconcileAsync(sealedPlan.PlanId, Now);
        Assert.Equal(WorkflowAlterationPlanStatus.Completed, completed.Status);
        Assert.Equal(2, completed.SucceededJobCount);
    }

    [Fact]
    public async Task Sqlite_continuations_are_bound_and_pages_are_provider_bounded()
    {
        await using var fixture = Fixture.Create();
        var tenantA = fixture.Store("tenant-a");
        var tenantB = fixture.Store("tenant-b");
        var planA = (await tenantA.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"))).Plan;
        var planB = (await tenantA.AdmitAsync(Plan("plan-b", "tenant-a", "key-b", "canonical-b"))).Plan;
        var foreignPlan = (await tenantB.AdmitAsync(Plan("plan-c", "tenant-b", "key-c", "canonical-c"))).Plan;
        var capturedA = await tenantA.CaptureAsync(planA.PlanId, planA.Revision,
        [
            new WorkflowAlterationCapturedTarget("execution-a", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")
        ], null);
        await tenantA.CaptureAsync(planB.PlanId, planB.Revision,
            [new WorkflowAlterationCapturedTarget("execution-c", "tenant-a")], null);
        await tenantB.CaptureAsync(foreignPlan.PlanId, foreignPlan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-d", "tenant-b")], null);

        var jobPage = await tenantA.PageJobsAsync(capturedA.PlanId, 1);
        Assert.NotNull(jobPage.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tenantA.PageJobsAsync(planB.PlanId, 1, jobPage.NextCursor).AsTask());

        var activePage = await tenantA.ListActivePlansAsync(1);
        Assert.NotNull(activePage.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tenantB.ListActivePlansAsync(1, activePage.NextCursor).AsTask());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            tenantA.PageJobsAsync(planA.PlanId, 2_001).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            tenantA.ListActivePlansAsync(2_001).AsTask());
    }

    [Fact]
    public void Composite_identities_are_boundary_safe()
    {
        Assert.NotEqual(
            GroundworkV2WorkflowAlterationStorageConventions.TenantIdempotencyKey("a\u001fb", "c"),
            GroundworkV2WorkflowAlterationStorageConventions.TenantIdempotencyKey("a", "b\u001fc"));
        Assert.NotEqual(
            GroundworkV2WorkflowAlterationStorageConventions.CreateJobId("a\u001fb", "c"),
            GroundworkV2WorkflowAlterationStorageConventions.CreateJobId("a", "b\u001fc"));
    }

    [Fact]
    public async Task Sqlite_cancellation_paging_and_scope_integrity_are_fail_closed()
    {
        await using var fixture = Fixture.Create();
        var tenantA = fixture.Store("tenant-a");
        var tenantB = fixture.Store("tenant-b");
        var plan = (await tenantA.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"))).Plan;
        await tenantA.CaptureAsync(plan.PlanId, plan.Revision, [
            new WorkflowAlterationCapturedTarget("execution-a", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")
        ], null);
        var cancelling = await tenantA.RequestCancellationAsync(plan.PlanId, Now);
        var sealedPlan = await tenantA.SealAsync(plan.PlanId, cancelling.Revision, Now);
        var skipped = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, Now);
        await tenantA.CancelPendingJobsAsync(sealedPlan.PlanId, [skipped], Now, 1);
        var stillCancelling = await tenantA.ReconcileAsync(sealedPlan.PlanId, Now);
        await tenantA.CancelPendingJobsAsync(sealedPlan.PlanId, [skipped], Now, 1);
        var completed = await tenantA.ReconcileAsync(sealedPlan.PlanId, Now);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, stillCancelling.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, completed.Status);
        Assert.Equal(2, completed.CancelledJobCount);
        Assert.Null(await tenantB.FindPlanAsync(plan.PlanId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.CaptureAsync(plan.PlanId, plan.Revision, [], null).AsTask());
    }

    [Fact]
    public async Task Sqlite_concurrent_admission_materializes_one_tenant_idempotency_winner()
    {
        await using var fixture = Fixture.Create();
        var firstStore = fixture.Store("tenant-a");
        var secondStore = fixture.Store("tenant-a");
        using var gate = new Barrier(2);
        var admissions = await Task.WhenAll(
            Task.Run(async () =>
            {
                gate.SignalAndWait();
                return await firstStore.AdmitAsync(Plan("plan-a", "tenant-a", "shared-key", "canonical-a"));
            }),
            Task.Run(async () =>
            {
                gate.SignalAndWait();
                return await secondStore.AdmitAsync(Plan("plan-b", "tenant-a", "shared-key", "canonical-a"));
            }));

        Assert.Single(admissions, admission => !admission.IsReplay);
        Assert.Single(admissions, admission => admission.IsReplay);
        Assert.Single(admissions.Select(admission => admission.Plan.PlanId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sqlite_terminal_replay_and_stale_claim_are_fenced()
    {
        await using var fixture = Fixture.Create();
        var store = fixture.Store("tenant-a");
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"))).Plan;
        var capture = await store.CaptureAsync(plan.PlanId, plan.Revision, [new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")], null);
        var sealedPlan = await store.SealAsync(plan.PlanId, capture.Revision, Now);
        var first = await store.ClaimNextAsync(sealedPlan.PlanId, "worker-a", Now, TimeSpan.FromSeconds(1));
        Assert.NotNull(first);
        var second = await store.ClaimNextAsync(sealedPlan.PlanId, "worker-b", Now.AddSeconds(1), TimeSpan.FromSeconds(1));
        Assert.NotNull(second);
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            second.JobId, first.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now)).AsTask());
        await store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            second.JobId, second.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now));
        await store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            second.JobId, second.Claim.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            second.JobId, second.Claim.Token, WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Different", null, Now)], "checkpoint-a", Now)).AsTask());
    }

    [Fact]
    public async Task Sqlite_missing_plan_counts_and_pages_fail_closed()
    {
        await using var fixture = Fixture.Create();
        var store = fixture.Store("tenant-a");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.GetJobCountsAsync("missing-plan").AsTask());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.PageJobsAsync("missing-plan", 10).AsTask());
    }

    [Fact]
    public async Task Sqlite_unsealed_capture_terminalization_deletes_provisional_jobs_atomically()
    {
        await using var fixture = Fixture.Create();
        var store = fixture.Store("tenant-a");
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, [
            new WorkflowAlterationCapturedTarget("execution-a", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")
        ], "cursor");
        var failed = await store.FailUnsealedCaptureAsync(captured.PlanId, new WorkflowAlterationSafeFailure("CaptureFailed", "safe"), Now);
        var page = await store.PageJobsAsync(captured.PlanId, 10);

        Assert.Equal(WorkflowAlterationPlanStatus.Failed, failed.Status);
        Assert.Equal(0, failed.TargetCount);
        Assert.Empty(page.Items);
        Assert.Null(failed.CaptureCursor);
    }

    [Fact]
    public async Task Sqlite_active_plan_order_checkpoint_lookup_and_privileged_scope_are_current_only()
    {
        await using var fixture = Fixture.Create();
        var tenantA = fixture.Store("tenant-a");
        var tenantB = fixture.Store("tenant-b");
        var planA = (await tenantA.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a"))).Plan;
        await tenantB.AdmitAsync(Plan("plan-b", "tenant-b", "key-b", "canonical-b"));

        var scopedPage = await tenantA.ListActivePlansAsync(10);
        Assert.Equal([planA.PlanId], scopedPage.Items.Select(plan => plan.PlanId));
        await tenantA.RescheduleActivePlanAsync(planA.PlanId, Now.AddHours(1));
        var privileged = fixture.Store(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("alteration-test")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.ListActivePlansAsync(10).AsTask());

        var captured = await tenantA.CaptureAsync(planA.PlanId, planA.Revision, [
            new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")
        ], null);
        var sealedPlan = await tenantA.SealAsync(captured.PlanId, captured.Revision, Now);
        var claimed = await tenantA.ClaimNextAsync(sealedPlan.PlanId, "worker-a", Now, TimeSpan.FromMinutes(1));
        Assert.NotNull(claimed);
        var change = new WorkflowAlterationJobTerminalChange(
            claimed.JobId,
            claimed.Claim!.Token,
            WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now)],
            "checkpoint-current",
            Now);
        await tenantA.ValidateTerminalJobChangeAsync(change);
        await tenantA.ApplyTerminalJobChangeAsync(change);

        var byCheckpoint = await tenantA.FindJobByCheckpointCommitIdAsync("checkpoint-current");
        Assert.Equal(claimed.JobId, byCheckpoint!.JobId);
        Assert.Null(await tenantB.FindJobByCheckpointCommitIdAsync("checkpoint-current"));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_atomic_alteration_lifecycle_is_current_and_fail_closed(string providerName)
    {
        var connectionString = providerName == "sqlite"
            ? null
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        RequireOrSkip(providerName != "sqlite" && string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} proof.");
        await using var fixture = NativeFixture.Create(providerName, connectionString);
        RequireOrSkip(
            !fixture.Connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
            $"The {providerName} provider does not advertise atomic commit (transactional Mongo is required).");

        var store = fixture.Store($"tenant-{providerName}");
        var admission = await store.AdmitAsync(Plan("plan-native", $"tenant-{providerName}", "key-native", "canonical-native"));
        var captured = await store.CaptureAsync(
            admission.Plan.PlanId,
            admission.Plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-native", $"tenant-{providerName}")],
            null);
        var sealedPlan = await store.SealAsync(captured.PlanId, captured.Revision, Now);
        var claimed = await store.ClaimNextAsync(sealedPlan.PlanId, "native-worker", Now, TimeSpan.FromMinutes(1));
        Assert.NotNull(claimed);
        await store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(
            claimed.JobId,
            claimed.Claim!.Token,
            WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now)],
            $"checkpoint-{providerName}",
            Now));
        var completed = await store.ReconcileAsync(sealedPlan.PlanId, Now);

        Assert.Equal(WorkflowAlterationPlanStatus.Completed, completed.Status);
        Assert.Equal(1, completed.SucceededJobCount);
        Assert.Equal(BatchWriteOptions.Exact, fixture.LastUnitOfWorkOptions);
        Assert.Contains(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind, fixture.LastUnitOfWorkUnitIds!);
        Assert.Contains(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind, fixture.LastUnitOfWorkUnitIds!);
    }

    private static WorkflowAlterationPlanState Plan(string id, string tenant, string key, string canonical) =>
        WorkflowAlterationPlanState.CreateCapturing(
            id,
            new WorkflowAlterationAuthorityScope(tenant, "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            key,
            canonical,
            new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["example"]),
            Now,
            [new WorkflowAlterationDescriptor("CancelWorkflow", 1, "Cancel workflow")]);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"elsa-runtime-alteration-{Guid.NewGuid():N}.db");
        private readonly IStorageProviderConnection connection;

        private Fixture()
        {
            connection = new SqliteProviderFactory().Create($"Data Source={path}");
            connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind));
            connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind));
        }

        public static Fixture Create() => new();

        public GroundworkV2WorkflowAlterationStore Store(string tenant) => Store(
            PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

        public GroundworkV2WorkflowAlterationStore Store(PersistenceAccessContext context) => new(
            new DirectSource(connection),
            new AccessAccessor(context));

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

        public BatchWriteOptions? LastUnitOfWorkOptions => source.LastUnitOfWorkOptions;

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds => source.LastUnitOfWorkUnitIds;

        public static NativeFixture Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-v2-alteration-{Guid.NewGuid():N}.db");
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
                    ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind or
                    ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind)
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

        public GroundworkV2WorkflowAlterationStore Store(string tenant) => new(
            source,
            new AccessAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

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

        public IReadOnlyList<string>? LastUnitOfWorkUnitIds { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(units[unitId], access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            LastUnitOfWorkOptions = options;
            LastUnitOfWorkUnitIds = unitIds.ToArray();
            return connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => units[unitId]).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
    }

    private sealed class DirectSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;
    }

    private sealed class AccessAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
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
