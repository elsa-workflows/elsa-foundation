using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowAlterationAndScopeTests
{
    private static readonly PersistenceAccessContext PrivilegedScoped =
        PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance"));

    [Fact]
    public void Registrations_are_opt_in_and_provider_guarded()
    {
        var services = new ServiceCollection(); services.AddWorkflowRuntime();
        services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) });
        services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        Assert.IsType<ServiceDescriptor>(services.Single(x => x.ServiceType == typeof(IWorkflowAlterationStore)));
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.EntityFramework, RuntimeWorkflowAlterationStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(services)!.Name);
    }

    [Fact]
    public async Task Registrations_are_order_independent_and_foreign_ownership_is_not_mutated()
    {
        var alterationFirst = new ServiceCollection();
        alterationFirst.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) });
        alterationFirst.AddWorkflowRuntime();
        Assert.Equal(typeof(EfWorkflowAlterationStore), alterationFirst.Single(x => x.ServiceType == typeof(EfWorkflowAlterationStore)).ImplementationType);

        var scopeFirst = new ServiceCollection();
        scopeFirst.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        scopeFirst.AddWorkflowRuntime();
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(scopeFirst)!.Name);
        Assert.Equal(typeof(EfWorkflowTestScopeCleanupStore), scopeFirst.Single(x => x.ServiceType == typeof(EfWorkflowTestScopeCleanupStore)).ImplementationType);

        var coreFirstScope = new ServiceCollection();
        coreFirstScope.AddWorkflowRuntime();
        coreFirstScope.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(coreFirstScope)!.Name);
        Assert.Equal(typeof(EfWorkflowTestScopeCleanupStore), coreFirstScope.Single(x => x.ServiceType == typeof(EfWorkflowTestScopeCleanupStore)).ImplementationType);
        Assert.Equal(typeof(EfWorkflowTestScopeStore), coreFirstScope.Single(x => x.ServiceType == typeof(EfWorkflowTestScopeStore)).ImplementationType);

        var sharedDatabasePath = Path.Combine(Path.GetTempPath(), $"elsa-runtime-order-{Guid.NewGuid():N}.db");
        try
        {
            var scopeThenAlteration = new ServiceCollection();
            scopeThenAlteration.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
            {
                ConnectionString = $"Data Source={sharedDatabasePath}",
                RecoveryContinuationSigningKey = new string('k', 32)
            });
            scopeThenAlteration.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
            {
                ConnectionString = $"Data Source={sharedDatabasePath}",
                RecoveryContinuationSigningKey = new string('k', 32)
            });
            scopeThenAlteration.AddWorkflowRuntime();

            Assert.Single(scopeThenAlteration, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
            Assert.Single(scopeThenAlteration, descriptor => descriptor.ServiceType == typeof(DbContextOptions<BookmarkStateSqliteDbContext>));

            await using var serviceProvider = scopeThenAlteration.BuildServiceProvider();
            await using var operationScope = serviceProvider.CreateAsyncScope();
            operationScope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var context = operationScope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            await context.Database.EnsureCreatedAsync();
            var alterationStore = operationScope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>();
            var testScopeStore = operationScope.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>();
            var plan = Plan("scope-first-plan");
            var workflowTestScope = new WorkflowTestScope(
                "scope-first-scope",
                DateTimeOffset.UtcNow.AddHours(1),
                "tenant-a",
                new WorkflowExecutionPartition("partition"));

            Assert.False((await alterationStore.AdmitAsync(plan)).IsReplay);
            Assert.Equal(workflowTestScope.ScopeId, (await testScopeStore.CreateAsync(workflowTestScope, DateTimeOffset.UtcNow)).Scope.ScopeId);
            Assert.Equal(plan.PlanId, (await alterationStore.FindPlanAsync(plan.PlanId))!.PlanId);
            Assert.Equal(workflowTestScope.ScopeId, (await testScopeStore.FindAsync(workflowTestScope.ScopeId))!.Scope.ScopeId);
        }
        finally
        {
            if (File.Exists(sharedDatabasePath))
                File.Delete(sharedDatabasePath);
        }

        var foreign = new ServiceCollection();
        foreign.AddScoped<IWorkflowAlterationStore, ForeignAlterationStore>();
        var before = foreign.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreign.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) }));
        Assert.Equal(before, foreign);

        var foreignAfterCore = new ServiceCollection();
        foreignAfterCore.AddWorkflowRuntime();
        foreignAfterCore.AddScoped<IWorkflowAlterationStore, ForeignAlterationStore>();
        before = foreignAfterCore.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreignAfterCore.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) }));
        Assert.Equal(before, foreignAfterCore);

        var foreignScopeBeforeCore = new ServiceCollection();
        foreignScopeBeforeCore.AddScoped<IWorkflowTestScopeStore, ForeignScopeStore>();
        before = foreignScopeBeforeCore.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreignScopeBeforeCore.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) }));
        Assert.Equal(before, foreignScopeBeforeCore);

        var foreignScopeAfterCore = new ServiceCollection();
        foreignScopeAfterCore.AddWorkflowRuntime();
        foreignScopeAfterCore.AddScoped<IWorkflowTestScopeStore, ForeignScopeStore>();
        before = foreignScopeAfterCore.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreignScopeAfterCore.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) }));
        Assert.Equal(before, foreignScopeAfterCore);

        var foreignAdmissionAfterCore = new ServiceCollection();
        foreignAdmissionAfterCore.AddWorkflowRuntime();
        foreignAdmissionAfterCore.AddScoped<IWorkflowTestScopeAdmissionStore, ForeignScopeAdmissionStore>();
        before = foreignAdmissionAfterCore.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreignAdmissionAfterCore.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) }));
        Assert.Equal(before, foreignAdmissionAfterCore);
    }

    [Fact]
    public void Repeated_registration_is_idempotent_and_conflicting_options_are_atomic()
    {
        var services = new ServiceCollection();
        var options = new RuntimeWorkflowAlterationEntityFrameworkCoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = new string('k', 32)
        };
        services.AddRuntimeWorkflowAlterationEntityFrameworkCore(options);
        var snapshot = services.ToArray();
        services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new RuntimeWorkflowAlterationEntityFrameworkCoreOptions
        {
            ConnectionString = options.ConnectionString,
            RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
        });
        Assert.Equal(snapshot, services);

        snapshot = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new RuntimeWorkflowAlterationEntityFrameworkCoreOptions
        {
            Provider = "SqlServer",
            ConnectionString = options.ConnectionString,
            RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
        }));
        Assert.Equal(snapshot, services);
    }

    [Fact]
    public void Sibling_registrations_reject_conflicting_recovery_keys_in_either_order()
    {
        var alterationKey = new string('a', 32);
        var scopeKey = new string('b', 32);
        var alterationFirst = new ServiceCollection();
        alterationFirst.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = alterationKey
        });
        var snapshot = alterationFirst.ToArray();

        Assert.Throws<InvalidOperationException>(() => alterationFirst.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = scopeKey
        }));
        Assert.Equal(snapshot, alterationFirst);

        var scopeFirst = new ServiceCollection();
        scopeFirst.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = scopeKey
        });
        snapshot = scopeFirst.ToArray();

        Assert.Throws<InvalidOperationException>(() => scopeFirst.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = alterationKey
        }));
        Assert.Equal(snapshot, scopeFirst);
    }

    [Fact]
    public async Task Alteration_missing_plan_queries_fail_closed()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Store.GetJobCountsAsync("missing").AsTask());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Store.PageJobsAsync("missing", 1).AsTask());
    }

    [Fact]
    public async Task Alteration_store_refuses_privileged_access_instead_of_reporting_no_plan()
    {
        await using var db = await Database.CreateAsync();
        await using var privileged = db.Open(PrivilegedScoped);
        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.Store.FindPlanAsync("missing").AsTask());
    }

    [Fact]
    public async Task Scope_store_refuses_privileged_access_instead_of_reporting_no_scope()
    {
        await using var db = await Database.CreateAsync();
        await using var privileged = db.Open(PrivilegedScoped);
        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.ScopeStore.FindAsync("missing").AsTask());
    }

    [Fact]
    public async Task Alteration_and_scope_materialized_tenant_mismatches_fail_closed()
    {
        await using var db = await Database.CreateAsync();
        var plan = Plan("tampered-plan");
        await using (var setup = db.Open("tenant-a"))
        {
            await setup.Store.AdmitAsync(plan);
            await setup.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        }

        await using (var corruptedPlan = db.Open("tenant-a"))
        {
            var row = await corruptedPlan.Context.WorkflowAlterationPlans.SingleAsync();
            row.ContentJson = row.ContentJson.Replace(EfRelationalIdentity.Encode("tenant-a"), EfRelationalIdentity.Encode("tenant-b"), StringComparison.Ordinal);
            var idempotency = "tenant-b\u001f" + plan.IdempotencyKeyHash;
            row.TenantIdempotencyKey = EfRelationalIdentity.Encode(idempotency);
            row.TenantIdempotencyKeyHash = EfRelationalIdentity.Hash(idempotency);
            await corruptedPlan.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => corruptedPlan.Store.FindPlanAsync(plan.PlanId).AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => corruptedPlan.Store.GetJobCountsAsync(plan.PlanId).AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => corruptedPlan.Store.PageJobsAsync(plan.PlanId, 1).AsTask());
        }

        await using (var corruptedJob = db.Open("tenant-a"))
        {
            var row = await corruptedJob.Context.WorkflowAlterationJobs.SingleAsync();
            row.ContentJson = row.ContentJson.Replace(EfRelationalIdentity.Encode("tenant-a"), EfRelationalIdentity.Encode("tenant-b"), StringComparison.Ordinal);
            row.TenantPartition = EfRelationalIdentity.Encode("tenant-b");
            row.TenantPartitionHash = EfRelationalIdentity.Hash("tenant-b");
            await corruptedJob.Context.SaveChangesAsync();
            var jobId = WorkflowAlterationIdentity.CreateJobId(plan.PlanId, "execution-1");
            await Assert.ThrowsAsync<InvalidDataException>(() => corruptedJob.Store.FindJobAsync(jobId).AsTask());
        }

        var scope = new WorkflowTestScope("tampered-scope", DateTimeOffset.UtcNow.AddHours(1), "tenant-a", new WorkflowExecutionPartition("partition"));
        await using (var setup = db.Open("tenant-a"))
            await setup.ScopeStore.CreateAsync(scope, DateTimeOffset.UtcNow);
        await using (var corruptedScope = db.Open("tenant-a"))
        {
            var row = await corruptedScope.Context.WorkflowTestScopes.SingleAsync();
            row.ContentJson = row.ContentJson.Replace(EfRelationalIdentity.Encode("tenant-a"), EfRelationalIdentity.Encode("tenant-b"), StringComparison.Ordinal);
            row.TenantId = EfRelationalIdentity.Encode("tenant-b");
            row.TenantIdHash = EfRelationalIdentity.Hash("tenant-b");
            await corruptedScope.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => corruptedScope.ScopeStore.FindAsync(scope.ScopeId).AsTask());
        }
    }

    [Fact]
    public async Task Alteration_pending_jobs_are_not_claimed_before_their_created_at()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan("claimable-at");
        await fixture.Store.AdmitAsync(plan);
        await fixture.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt);

        Assert.Null(await fixture.Store.ClaimNextAsync(plan.PlanId, "early-worker", plan.CreatedAt.AddTicks(-1), TimeSpan.FromMinutes(1)));
        Assert.NotNull(await fixture.Store.ClaimNextAsync(plan.PlanId, "on-time-worker", plan.CreatedAt, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Alteration_same_idempotency_admission_has_one_durable_winner()
    {
        await using var db = await Database.CreateAsync();
        var plan = Plan("concurrent-admission");
        await using var left = db.Open("tenant-a");
        await using var right = db.Open("tenant-a");
        var results = await Task.WhenAll(left.Store.AdmitAsync(plan).AsTask(), right.Store.AdmitAsync(plan).AsTask());
        Assert.Single(results, result => !result.IsReplay);
        Assert.Single(results, result => result.IsReplay);
        Assert.Equal(plan.PlanId, (await left.Store.FindPlanAsync(plan.PlanId))!.PlanId);
    }

    private sealed class ForeignAlterationStore : IWorkflowAlterationStore
    {
        public ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(WorkflowAlterationPlanState plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> CaptureAsync(string planId, long expectedRevision, IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets, string? nextCursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset sealedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask CancelPendingJobsAsync(string planId, IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes, DateTimeOffset completedAt, int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationJobPage> PageJobsAsync(string planId, int pageSize, string? cursor = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ForeignScopeStore : IWorkflowTestScopeStore
    {
        public ValueTask<WorkflowTestScopeRecord> CreateAsync(WorkflowTestScope scope, DateTimeOffset createdAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeRecord?> FindAsync(string scopeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(WorkflowTestScopeCloseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeRecord> CompleteAsync(string scopeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopePage> QueryAsync(WorkflowTestScopePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ForeignScopeAdmissionStore : IWorkflowTestScopeAdmissionStore
    {
        public ValueTask AssertOpenAsync(WorkflowTestScope scope, DateTimeOffset observedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Alteration_plan_and_jobs_round_trip_with_claim_and_terminal_evidence()
    {
        await using var db = await Database.CreateAsync(); await using var fixture = db.Open("tenant-a"); var plan = Plan();
        Assert.False((await fixture.Store.AdmitAsync(plan)).IsReplay); Assert.True((await fixture.Store.AdmitAsync(plan)).IsReplay);
        await fixture.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        var sealedPlan = await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1)); var job = await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2), TimeSpan.FromMinutes(1)); Assert.NotNull(job);
        var outcomes = new[]
        {
            new WorkflowAlterationOutcome(1, "second", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok-2", null, plan.CreatedAt),
            new WorkflowAlterationOutcome(0, "first", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok-1", null, plan.CreatedAt)
        };
        var change = new WorkflowAlterationJobTerminalChange(job!.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, outcomes, WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        await fixture.Store.ApplyTerminalJobChangeAsync(change); Assert.Equal(change.CheckpointCommitId, (await fixture.Store.FindJobByCheckpointCommitIdAsync(change.CheckpointCommitId))!.CheckpointCommitId); Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.Store.FindJobAsync(job.JobId))!.Status); Assert.Equal(1, (await fixture.Store.GetJobCountsAsync(plan.PlanId)).Succeeded); Assert.Equal(WorkflowAlterationPlanStatus.Completed, (await fixture.Store.ReconcileAsync(plan.PlanId, plan.CreatedAt.AddMinutes(4))).Status);
    }

    [Fact]
    public async Task Scope_lifecycle_admission_and_isolation_are_durable()
    {
        await using var db = await Database.CreateAsync(); await using var fixture = db.Open("tenant-a"); var scope = new WorkflowTestScope("scope", DateTimeOffset.UtcNow.AddHours(1), "tenant-a", new WorkflowExecutionPartition("partition")); var created = await fixture.ScopeStore.CreateAsync(scope, DateTimeOffset.UtcNow); await fixture.ScopeStore.AssertOpenAsync(scope, DateTimeOffset.UtcNow); var closing = await fixture.ScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, created.CreatedAt.AddMinutes(1))); Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, closing.Disposition); var completed = await fixture.ScopeStore.CompleteAsync(scope.ScopeId, created.CreatedAt.AddMinutes(2)); Assert.Equal(WorkflowTestScopeState.Closed, completed.State);
        await using (var restarted = db.Open("tenant-a"))
            Assert.Equal(WorkflowTestScopeState.Closed, (await restarted.ScopeStore.FindAsync(scope.ScopeId))!.State);
        await using var other = db.Open("tenant-b"); Assert.Null(await other.ScopeStore.FindAsync(scope.ScopeId));
    }

    [Fact]
    public async Task Scope_replay_conflict_and_stale_admission_are_durable()
    {
        await using var db = await Database.CreateAsync();
        await using var setup = db.Open("tenant-a");
        var createdAt = DateTimeOffset.UtcNow;
        var scope = new WorkflowTestScope("scope-replay", createdAt.AddHours(1), "tenant-a", new WorkflowExecutionPartition("partition"));
        var created = await setup.ScopeStore.CreateAsync(scope, createdAt);
        Assert.Equal(created, await setup.ScopeStore.CreateAsync(scope, createdAt.AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.ScopeStore.CreateAsync(
            new WorkflowTestScope(scope.ScopeId, scope.ExpiresAt.AddMinutes(1), scope.TenantId, scope.Partition), createdAt).AsTask());

        await using var left = db.Open("tenant-a");
        await using var right = db.Open("tenant-a");
        await left.Context.WorkflowTestScopes.SingleAsync();
        await right.ScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, createdAt.AddMinutes(2)));
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => left.ScopeStore.AssertOpenAsync(scope, createdAt.AddMinutes(2)).AsTask());
    }

    [Fact]
    public async Task Scope_continuations_are_stable_tamper_proof_and_tenant_bound()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var createdAt = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "scope-a", "scope-b", "scope-c" })
        {
            var scope = new WorkflowTestScope(id, createdAt.AddHours(1), "tenant-a", new WorkflowExecutionPartition("partition"));
            await fixture.ScopeStore.CreateAsync(scope, createdAt);
            await fixture.ScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(id, WorkflowTestScopeCloseReason.ExplicitTeardown, createdAt.AddMinutes(1)));
        }

        var first = await fixture.ScopeStore.QueryAsync(new WorkflowTestScopePageQuery(createdAt.AddMinutes(1), 1));
        Assert.Single(first.Items);
        Assert.NotNull(first.ContinuationToken);
        var second = await fixture.ScopeStore.QueryAsync(new WorkflowTestScopePageQuery(createdAt.AddMinutes(1), 1, ContinuationToken: first.ContinuationToken));
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Scope.ScopeId, second.Items[0].Scope.ScopeId);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.ScopeStore.QueryAsync(new WorkflowTestScopePageQuery(createdAt.AddMinutes(1), 1, ContinuationToken: first.ContinuationToken! + "x")).AsTask());

        await using var other = db.Open("tenant-b");
        await Assert.ThrowsAsync<ArgumentException>(() => other.ScopeStore.QueryAsync(new WorkflowTestScopePageQuery(createdAt.AddMinutes(1), 1, ContinuationToken: first.ContinuationToken)).AsTask());

        var row = await fixture.Context.WorkflowTestScopes.SingleAsync(x => x.ScopeId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("scope-a"));
        row.PartitionOrderKey = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.ScopeStore.FindAsync("scope-a").AsTask());
    }

    [Fact]
    public async Task Alteration_pages_are_bounded_stable_and_scope_bound()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        for (var i = 0; i < 3; i++)
        {
            var plan = Plan($"plan-{i}");
            await fixture.Store.AdmitAsync(plan);
        }

        var first = await fixture.Store.ListActivePlansAsync(2);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await fixture.Store.ListActivePlansAsync(2, first.NextCursor);
        Assert.Single(second.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListActivePlansAsync(2, first.NextCursor! + "x").AsTask());

        await using var other = db.Open("tenant-b");
        await Assert.ThrowsAsync<ArgumentException>(() => other.Store.ListActivePlansAsync(2, first.NextCursor).AsTask());
    }

    [Fact]
    public async Task Alteration_capture_replay_safe_failure_and_job_paging_preserve_identity()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan("capture-plan");
        await fixture.Store.AdmitAsync(plan);

        var captured = await fixture.Store.CaptureAsync(
            plan.PlanId,
            plan.Revision,
            [
                new WorkflowAlterationCapturedTarget("execution-live", "tenant-a"),
                new WorkflowAlterationCapturedTarget("execution-safe", "tenant-a", safeFailure: new("Missing", "not visible")),
                new WorkflowAlterationCapturedTarget("execution-live", "tenant-a")
            ],
            "capture-page-1");

        Assert.Equal(2, captured.CapturedSoFar);
        var safe = await fixture.Store.FindJobAsync(WorkflowAlterationIdentity.CreateJobId(plan.PlanId, "execution-safe"));
        Assert.NotNull(safe);
        Assert.Equal(WorkflowAlterationJobStatus.Failed, safe!.Status);
        Assert.Equal(0, safe.AttemptCount);
        Assert.Empty(safe.Outcomes);
        Assert.Equal(plan.CreatedAt, safe.CompletedAt);

        var firstPage = await fixture.Store.PageJobsAsync(plan.PlanId, 1);
        Assert.Single(firstPage.Items);
        Assert.True(firstPage.HasNext);
        var secondPage = await fixture.Store.PageJobsAsync(plan.PlanId, 1, firstPage.NextCursor);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].JobId, secondPage.Items[0].JobId);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.PageJobsAsync(plan.PlanId, 1, firstPage.NextCursor! + "x").AsTask());

        var otherPlan = Plan("other-plan");
        await fixture.Store.AdmitAsync(otherPlan);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.PageJobsAsync(otherPlan.PlanId, 1, firstPage.NextCursor).AsTask());

        var replay = await fixture.Store.CaptureAsync(
            plan.PlanId,
            captured.Revision,
            [
                new WorkflowAlterationCapturedTarget("execution-live", "tenant-a"),
                new WorkflowAlterationCapturedTarget("execution-safe", "tenant-a", safeFailure: new("Missing", "not visible"))
            ],
            "capture-page-2");
        Assert.Equal(2, replay.CapturedSoFar);
        Assert.Equal("capture-page-2", replay.CaptureCursor);
    }

    [Fact]
    public async Task Alteration_reschedule_preserves_domain_revision_and_reconcile_reports_failures()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan("state-plan");
        await fixture.Store.AdmitAsync(plan);
        var captured = await fixture.Store.CaptureAsync(plan.PlanId, plan.Revision, [
            new WorkflowAlterationCapturedTarget("execution-live", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-safe", "tenant-a", safeFailure: new("Missing"))
        ], null);
        var sealedPlan = await fixture.Store.SealAsync(plan.PlanId, captured.Revision, plan.CreatedAt.AddMinutes(1));
        await fixture.Store.RescheduleActivePlanAsync(plan.PlanId, plan.CreatedAt.AddMinutes(2));
        var rescheduled = await fixture.Store.FindPlanAsync(plan.PlanId);
        Assert.Equal(sealedPlan.Revision, rescheduled!.Revision);

        var claim = await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(3), TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);
        var completed = new WorkflowAlterationJobTerminalChange(
            claim!.JobId,
            claim.Claim!.Token,
            WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "test", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok", null, plan.CreatedAt)],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(claim.JobId, 1),
            plan.CreatedAt.AddMinutes(4));
        await fixture.Store.ApplyTerminalJobChangeAsync(completed);
        var reconciled = await fixture.Store.ReconcileAsync(plan.PlanId, plan.CreatedAt.AddMinutes(5));
        Assert.Equal(WorkflowAlterationPlanStatus.CompletedWithFailures, reconciled.Status);
        Assert.Equal(1, reconciled.SucceededJobCount);
        Assert.Equal(1, reconciled.FailedJobCount);
    }

    [Fact]
    public async Task Alteration_rejects_conflicting_idempotency_and_stale_terminal_fence()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan();
        await fixture.Store.AdmitAsync(plan);
        var conflicting = new WorkflowAlterationPlanState(plan.PlanId, plan.AuthorityScope, plan.SubmittedBy, plan.IdempotencyKeyHash, "different", plan.ProtectedPayload, plan.Target, plan.Status, plan.CreatedAt, alterationDescriptors: plan.AlterationDescriptors);
        await Assert.ThrowsAsync<WorkflowAlterationIdempotencyConflictException>(() => fixture.Store.AdmitAsync(conflicting).AsTask());
        await fixture.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await Assert.ThrowsAsync<WorkflowAlterationConcurrencyException>(() => fixture.Store.CaptureAsync(plan.PlanId, 0, [], null).AsTask());
        await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        var job = await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
        Assert.NotNull(job);
        var change = new WorkflowAlterationJobTerminalChange(job!.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [new WorkflowAlterationOutcome(0, "test", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok", null, plan.CreatedAt)], WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        var stale = new WorkflowAlterationJobTerminalChange(change.JobId, "stale", change.Status, change.Outcomes, change.CheckpointCommitId, change.CompletedAt);
        await Assert.ThrowsAsync<WorkflowAlterationClaimFenceException>(() => fixture.Store.ApplyTerminalJobChangeAsync(stale).AsTask());
        await fixture.Store.ApplyTerminalJobChangeAsync(change);
        await fixture.Store.ApplyTerminalJobChangeAsync(new WorkflowAlterationJobTerminalChange(change.JobId, change.ClaimToken, change.Status, change.Outcomes.Reverse().ToArray(), change.CheckpointCommitId, change.CompletedAt));
        var conflictingTerminal = new WorkflowAlterationJobTerminalChange(change.JobId, change.ClaimToken, WorkflowAlterationJobStatus.Failed, change.Outcomes, change.CheckpointCommitId, change.CompletedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyTerminalJobChangeAsync(conflictingTerminal).AsTask());
    }

    [Fact]
    public async Task Alteration_terminal_checkpoint_callback_fails_closed_without_mutation()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan();
        await fixture.Store.AdmitAsync(plan);
        await fixture.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        var job = await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
        var change = new WorkflowAlterationJobTerminalChange(job!.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [], WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CommitTerminalJobChangeAtomicallyAsync(change, _ => ValueTask.CompletedTask).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CommitTerminalJobChangeAtomicallyAsync(change,
            cancellationToken => fixture.Store.ApplyTerminalJobChangeAsync(change, cancellationToken)).AsTask());
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await fixture.Store.FindJobAsync(job.JobId))!.Status);
    }

    [Fact]
    public async Task Checkpoint_marker_terminalizes_claimed_alteration_job_in_the_same_ef_transaction()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan();
        await fixture.Store.AdmitAsync(plan);
        await fixture.Store.CaptureAsync(plan.PlanId, 0,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        var job = (await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2),
            TimeSpan.FromMinutes(1)))!;
        var terminal = new WorkflowAlterationJobTerminalChange(
            job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        var checkpoint = CheckpointFor(job, terminal);
        var store = new EfRuntimeCheckpointCommitStore(fixture.Context, new Accessor("tenant-a"));

        async ValueTask CommitCheckpointAsync(CancellationToken cancellationToken) =>
            _ = await store.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate), cancellationToken);
        await fixture.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync);
        await fixture.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync);
        var stored = (await fixture.Store.FindJobAsync(job.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, stored.Status);
        Assert.Equal(terminal.CheckpointCommitId, stored.CheckpointCommitId);
        Assert.Equal(job.Revision + 1, stored.Revision);
        Assert.Single(await fixture.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Alteration_callback_rejects_a_different_ef_context_before_the_checkpoint_writes()
    {
        await using var db = await Database.CreateAsync();
        await using var owner = db.Open("tenant-a");
        var plan = Plan("wrong-context-plan");
        await owner.Store.AdmitAsync(plan);
        await owner.Store.CaptureAsync(plan.PlanId, 0,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await owner.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        var job = (await owner.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2),
            TimeSpan.FromMinutes(1)))!;
        var terminal = new WorkflowAlterationJobTerminalChange(
            job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        var checkpoint = CheckpointFor(job, terminal);
        await using var foreign = db.Open("tenant-a");
        var foreignWriter = new EfRuntimeCheckpointCommitStore(foreign.Context, new Accessor("tenant-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            owner.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, async cancellationToken =>
            {
                await foreignWriter.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate), cancellationToken);
            }).AsTask());
        var wrongScopeWriter = new EfRuntimeCheckpointCommitStore(owner.Context, new Accessor("tenant-b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            owner.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, async cancellationToken =>
            {
                await wrongScopeWriter.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate), cancellationToken);
            }).AsTask());
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await owner.Store.FindJobAsync(job.JobId))!.Status);
        Assert.Empty(await owner.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Alteration_callback_marker_failure_rolls_back_job_and_retries_cleanly()
    {
        await using var db = await Database.CreateAsync();
        var interceptor = new MarkerInsertFailureInterceptor();
        await using var fixture = db.Open("tenant-a", interceptor);
        var plan = Plan("marker-rollback-plan");
        await fixture.Store.AdmitAsync(plan);
        await fixture.Store.CaptureAsync(plan.PlanId, 0,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await fixture.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        var job = (await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", plan.CreatedAt.AddMinutes(2),
            TimeSpan.FromMinutes(1)))!;
        var terminal = new WorkflowAlterationJobTerminalChange(
            job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1), plan.CreatedAt.AddMinutes(3));
        var checkpoint = CheckpointFor(job, terminal);
        var writer = new EfRuntimeCheckpointCommitStore(fixture.Context, new Accessor("tenant-a"));
        async ValueTask CommitCheckpointAsync(CancellationToken cancellationToken) =>
            _ = await writer.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate), cancellationToken);

        interceptor.Arm();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await fixture.Store.FindJobAsync(job.JobId))!.Status);
        Assert.Empty(await fixture.Context.RuntimeCheckpointCommits.ToArrayAsync());

        await fixture.Store.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.Store.FindJobAsync(job.JobId))!.Status);
        Assert.Single(await fixture.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Alteration_unsealed_cleanup_is_bounded_and_preserves_first_terminal_intent()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan("cleanup-plan");
        await fixture.Store.AdmitAsync(plan);
        var targets = Enumerable.Range(0, RuntimeWorkflowAlterationEfModule.UnsealedCleanupPageSize + 5)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index}", "tenant-a"))
            .ToArray();
        var captured = await fixture.Store.CaptureAsync(plan.PlanId, plan.Revision, targets, null);

        var first = await fixture.Store.CancelUnsealedCaptureAsync(plan.PlanId, plan.CreatedAt.AddMinutes(1));
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, first.Status);
        Assert.Equal(5, (await fixture.Store.GetJobCountsAsync(plan.PlanId)).Total);

        var safeFailure = new WorkflowAlterationSafeFailure("capture-failed", "late failure must not replace cancellation");
        var interleaved = await fixture.Store.FailUnsealedCaptureAsync(plan.PlanId, safeFailure, plan.CreatedAt.AddMinutes(2));
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, interleaved.Status);
        var completed = await fixture.Store.CancelUnsealedCaptureAsync(plan.PlanId, plan.CreatedAt.AddMinutes(3));
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, completed.Status);
        Assert.Null(completed.SafeFailure);
        Assert.Equal(0, (await fixture.Store.GetJobCountsAsync(plan.PlanId)).Total);
        Assert.Null((await fixture.Store.FindPlanAsync(plan.PlanId))!.CaptureCursor);
    }

    [Fact]
    public async Task Alteration_cancellation_cancels_pending_jobs_in_bounded_batches()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var plan = Plan("cancel-jobs-plan");
        await fixture.Store.AdmitAsync(plan);
        var captured = await fixture.Store.CaptureAsync(plan.PlanId, plan.Revision, [
            new WorkflowAlterationCapturedTarget("execution-1", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-2", "tenant-a"),
            new WorkflowAlterationCapturedTarget("execution-3", "tenant-a")
        ], null);
        await fixture.Store.SealAsync(plan.PlanId, captured.Revision, plan.CreatedAt.AddMinutes(1));
        await fixture.Store.RequestCancellationAsync(plan.PlanId, plan.CreatedAt.AddMinutes(2));
        var skipped = new[] { new WorkflowAlterationOutcome(0, "cancelled", 1, WorkflowAlterationOutcomeStatus.Skipped, "skip", null, plan.CreatedAt) };
        await fixture.Store.CancelPendingJobsAsync(plan.PlanId, skipped, plan.CreatedAt.AddMinutes(3), 2);
        Assert.Equal(1, (await fixture.Store.GetJobCountsAsync(plan.PlanId)).Pending);
        await fixture.Store.CancelPendingJobsAsync(plan.PlanId, skipped, plan.CreatedAt.AddMinutes(4), 2);
        var counts = await fixture.Store.GetJobCountsAsync(plan.PlanId);
        Assert.Equal(0, counts.Pending);
        Assert.Equal(3, counts.Cancelled);
    }

    [Fact]
    public async Task Competing_claimers_have_one_winner_and_expired_claim_is_fenced()
    {
        await using var db = await Database.CreateAsync();
        await using var setup = db.Open("tenant-a");
        var plan = Plan();
        await setup.Store.AdmitAsync(plan);
        await setup.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], null);
        await setup.Store.SealAsync(plan.PlanId, 1, plan.CreatedAt.AddMinutes(1));
        await using var left = db.Open("tenant-a");
        await using var right = db.Open("tenant-a");
        var claims = await Task.WhenAll(
            left.Store.ClaimNextAsync(plan.PlanId, "left", plan.CreatedAt.AddMinutes(2), TimeSpan.FromMinutes(1)).AsTask(),
            right.Store.ClaimNextAsync(plan.PlanId, "right", plan.CreatedAt.AddMinutes(2), TimeSpan.FromMinutes(1)).AsTask());
        var winner = Assert.Single(claims, x => x is not null);
        Assert.Contains(claims, x => x is null);
        var reclaimed = await left.Store.ClaimNextAsync(plan.PlanId, "reclaimer", plan.CreatedAt.AddHours(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(reclaimed);
        var stale = new WorkflowAlterationJobTerminalChange(winner!.JobId, winner.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [], WorkflowAlterationIdentity.CreateCheckpointCommitId(winner.JobId, 2), plan.CreatedAt.AddHours(1));
        await Assert.ThrowsAsync<WorkflowAlterationClaimFenceException>(() => left.Store.ApplyTerminalJobChangeAsync(stale).AsTask());
    }

    [Fact]
    public async Task Scope_expiry_query_and_idempotent_close_are_durable()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var createdAt = DateTimeOffset.UtcNow;
        var scope = new WorkflowTestScope("expired", createdAt.AddMinutes(1), "tenant-a", new WorkflowExecutionPartition("partition"));
        await fixture.ScopeStore.CreateAsync(scope, createdAt);
        var page = await fixture.ScopeStore.QueryAsync(new WorkflowTestScopePageQuery(scope.ExpiresAt, 1));
        Assert.Single(page.Items);
        var close = await fixture.ScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.Expired, scope.ExpiresAt));
        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, close.Disposition);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosing, (await fixture.ScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.Expired, scope.ExpiresAt))).Disposition);
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => fixture.ScopeStore.AssertOpenAsync(scope, scope.ExpiresAt).AsTask());
    }

    [Fact]
    public async Task Scope_query_rejects_an_unbounded_page_request()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");

        var query = new WorkflowTestScopePageQuery(
            DateTimeOffset.UtcNow,
            RuntimeWorkflowTestScopeEfModule.MaximumPageSize + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.ScopeStore.QueryAsync(query).AsTask());
    }

    [Fact]
    public async Task Alteration_active_plan_paging_is_provider_neutral_for_case_sensitive_ids()
    {
        await using var db = await Database.CreateAsync();
        await using var fixture = db.Open("tenant-a");
        var createdAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        await fixture.Store.AdmitAsync(Plan("aa", createdAt));
        await fixture.Store.AdmitAsync(Plan("aG", createdAt));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await fixture.Store.ListActivePlansAsync(1, cursor);
            foreach (var plan in page.Items.Where(plan => plan.PlanId is "aa" or "aG"))
                Assert.True(seen.Add(plan.PlanId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(["aG", "aa"], seen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Restart_round_trip_and_projection_corruption_fail_closed()
    {
        await using var db = await Database.CreateAsync();
        var plan = Plan();
        await using (var first = db.Open("tenant-a"))
            await first.Store.AdmitAsync(plan);
        await using var restarted = db.Open("tenant-a");
        Assert.Equal(plan.PlanId, (await restarted.Store.FindPlanAsync(plan.PlanId))!.PlanId);
        var row = await restarted.Context.WorkflowAlterationPlans.SingleAsync();
        row.ActiveOrderKey = "corrupt";
        await restarted.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.Store.FindPlanAsync(plan.PlanId).AsTask());
    }

    [Fact]
    public async Task Alteration_keys_keep_delimiter_colliding_scope_and_plan_pairs_separate()
    {
        await using var db = await Database.CreateAsync();
        const string firstScope = "tenant-a";
        const string firstPlanId = "plan\u001fx";
        const string secondScope = "tenant-a\u001fplan";
        const string secondPlanId = "x";
        Assert.Equal(EfRelationalIdentity.Hash(firstScope + "\u001f" + firstPlanId),
            EfRelationalIdentity.Hash(secondScope + "\u001f" + secondPlanId));
        await using (var first = db.Open(firstScope))
            await first.Store.AdmitAsync(Plan(firstPlanId, tenant: firstScope));
        await using (var second = db.Open(secondScope))
            await second.Store.AdmitAsync(Plan(secondPlanId, tenant: secondScope));
        await using (var first = db.Open(firstScope))
        {
            Assert.Equal(firstPlanId, (await first.Store.FindPlanAsync(firstPlanId))!.PlanId);
            Assert.Null(await first.Store.FindPlanAsync(secondPlanId));
        }
        await using (var second = db.Open(secondScope))
        {
            Assert.Equal(secondPlanId, (await second.Store.FindPlanAsync(secondPlanId))!.PlanId);
            Assert.Null(await second.Store.FindPlanAsync(firstPlanId));
        }
    }

    private static WorkflowAlterationPlanState Plan(string id = "plan-1", DateTimeOffset? createdAt = null, string tenant = "tenant-a") { var now = createdAt ?? DateTimeOffset.UtcNow; return WorkflowAlterationPlanState.CreateCapturing(id, new(tenant, "system", "root"), new("subject", "correlation"), "idem-" + id, "canonical-" + id, new("key", "AES", "cipher"), WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"]), now); }
    private static RuntimeCheckpointCommit CheckpointFor(
        WorkflowAlterationJobState job, WorkflowAlterationJobTerminalChange terminal) => new(
        terminal.CheckpointCommitId,
        new RuntimeCheckpoint($"checkpoint:{terminal.CheckpointCommitId}", "runtime.alteration.job",
            job.WorkflowExecutionId, terminal.CompletedAt, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
            null, null, null, null, null, null, terminal),
        [], new Dictionary<string, string>());
    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper; private readonly string _cs;
        private Database(SqliteConnection keeper, string cs) { _keeper = keeper; _cs = cs; }
        public static async Task<Database> CreateAsync() { var cs = $"Data Source=file:alteration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"; var keeper = new SqliteConnection(cs); await keeper.OpenAsync(); await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options); await context.Database.EnsureCreatedAsync(); return new(keeper, cs); }
        public Fixture Open(string scope, DbCommandInterceptor? interceptor = null) => Open(PersistenceAccessContext.Scoped(new PersistenceScope(scope)), interceptor);
        public Fixture Open(PersistenceAccessContext access, DbCommandInterceptor? interceptor = null) => new(_cs, access, interceptor); public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection; public readonly BookmarkStateSqliteDbContext Context; public readonly EfWorkflowAlterationStore Store; public readonly EfWorkflowTestScopeStore ScopeStore;
        public Fixture(string cs, PersistenceAccessContext accessContext, DbCommandInterceptor? interceptor = null) { _connection = new SqliteConnection(cs); _connection.Open(); var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(_connection); if (interceptor is not null) options.AddInterceptors(interceptor); Context = new BookmarkStateSqliteDbContext(options.Options); var access = new Accessor(accessContext); var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })); Store = new(Context, access, codec); ScopeStore = new(Context, access, codec); }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public Accessor(string value) : this(PersistenceAccessContext.Scoped(new PersistenceScope(value))) { }
        public PersistenceAccessContext Current { get; } = current;
    }
    private sealed class MarkerInsertFailureInterceptor : DbCommandInterceptor
    {
        private int armed;
        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed(DbCommand command)
        {
            if (command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new DbUpdateException("Simulated alteration checkpoint marker insert failure.");
        }
    }
}
