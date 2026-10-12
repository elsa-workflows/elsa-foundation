using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Data.Common;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowTestScopeCleanupStoreTests
{
    private const string CancelChildIntentKind = "Elsa.Activities.DispatchWorkflow.CancelChild";
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registration_owns_the_complete_test_scope_contract_family()
    {
        var services = new ServiceCollection();
        services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = new string('k', 32)
        });

        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(services)!.Name);
        Assert.Equal(typeof(EfWorkflowTestScopeCleanupStore),
            services.Single(descriptor => descriptor.ServiceType == typeof(EfWorkflowTestScopeCleanupStore)).ImplementationType);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
        Assert.True(WorkflowTestScopeStoreBackend.Find(services)!.Owns(
            services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore))));
    }

    [Fact]
    public async Task Cleanup_continuation_is_bound_to_the_exact_scope_before_database_access()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var source = Scope("cleanup-token-source");
        var target = Scope("cleanup-token-target");
        await scopes.CreateAsync(source, Now);
        await scopes.CreateAsync(target, Now);
        await dispatches.SaveAsync(Dispatch("source-first", source));
        await dispatches.SaveAsync(Dispatch("source-second", source, Now.AddSeconds(1)));
        var targetDispatch = Dispatch("target-first", target);
        await dispatches.SaveAsync(targetDispatch);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            source.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            target.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        var first = await cleanup.CleanupAsync(source, Now.AddMinutes(1), 1, new Dictionary<string, RuntimePostCommitIntent>());
        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.CleanupAsync(
            target, Now.AddMinutes(1), 1, new Dictionary<string, RuntimePostCommitIntent>(), first.ContinuationToken).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(targetDispatch.DispatchId))!.Status);
    }

    [Fact]
    public async Task Cleanup_cancellation_is_checked_before_any_mutation()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-cancelled");
        await scopes.CreateAsync(scope, Now);
        var dispatch = Dispatch("cancelled", scope);
        await dispatches.SaveAsync(dispatch);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>(), cancellationToken: cancellation.Token).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(dispatch.DispatchId))!.Status);
    }

    [Fact]
    public async Task Cleanup_rejects_tampered_scope_projection_before_mutating_dispatches()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-tampered-scope");
        await scopes.CreateAsync(scope, Now);
        var dispatch = Dispatch("tampered-scope", scope);
        await dispatches.SaveAsync(dispatch);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        var row = await context.WorkflowTestScopes.SingleAsync();
        row.ScopeId = EfRelationalIdentity.Encode("tampered-scope-id");
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>()).AsTask());
        Assert.Equal((int)WorkflowDispatchStatus.Pending,
            (await context.WorkflowDispatches.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Cleanup_rejects_tampered_dispatch_projection_instead_of_treating_it_as_missing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-tampered-dispatch");
        await scopes.CreateAsync(scope, Now);
        var dispatch = Dispatch("tampered-dispatch", scope);
        await dispatches.SaveAsync(dispatch);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        var row = await context.WorkflowDispatches.SingleAsync();
        row.DispatchId = EfRelationalIdentity.Encode("tampered-dispatch-id");
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>()).AsTask());
        Assert.Equal((int)WorkflowDispatchStatus.Pending,
            (await context.WorkflowDispatches.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Cleanup_rejects_tampered_existing_outbox_projection_atomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-tampered-outbox");
        await scopes.CreateAsync(scope, Now);
        var dispatch = Dispatch("tampered-outbox", scope, Now.AddSeconds(1));
        await dispatches.SaveAsync(dispatch);
        var started = dispatch.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        var intent = CancellationIntent(started, Now.AddMinutes(1));
        var outboxId = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId).ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        await outbox.SavePendingAsync(new RuntimePostCommitOutboxItem(
            outboxId,
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            Now.AddMinutes(1),
            Now.AddMinutes(1),
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))));
        var row = await context.RuntimePostCommitOutbox.SingleAsync();
        row.OutboxItemId = EfRelationalIdentity.Encode("tampered-outbox-id");
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent }).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Started,
            (await dispatches.FindAsync(started.DispatchId))!.Status);
        Assert.Equal(EfRelationalIdentity.Encode("tampered-outbox-id"),
            (await context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).OutboxItemId);
    }

    [Fact]
    public async Task Sqlite_cleanup_cancels_detached_dispatches_and_replays_deterministically()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-basic");
        await scopes.CreateAsync(scope, Now);

        var pending = Dispatch("pending", scope);
        var started = Dispatch("started", scope, Now.AddSeconds(1));
        await dispatches.SaveAsync(pending);
        await dispatches.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await dispatches.SaveAsync(Dispatch("waited", scope, Now.AddSeconds(3), WorkflowDispatchMode.WaitForCompletion));
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        var intent = CancellationIntent(started, Now.AddMinutes(1));
        var result = await cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });

        Assert.Equal(2, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(1, result.CancellationQueued);
        Assert.Equal(1, result.RemainingLive);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        var marked = (await dispatches.FindAsync(started.DispatchId))!;
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(marked));
        var outboxId = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId).ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        var item = await outbox.FindAsync(outboxId);
        Assert.NotNull(item);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item!.Status);

        var replay = await cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = intent });
        Assert.Equal(0, replay.Inspected);
        Assert.Equal(1, replay.RemainingLive);
        var replayedItem = await outbox.FindAsync(outboxId);
        Assert.NotNull(replayedItem);
        Assert.Equal(item.Intent.IntentId, replayedItem!.Intent.IntentId);
        Assert.Equal(item.Intent.IdempotencyKey, replayedItem.Intent.IdempotencyKey);
        Assert.Equal(item.Status, replayedItem.Status);
        Assert.Equal(item.RecordedAt, replayedItem.RecordedAt);
    }

    [Fact]
    public async Task Sqlite_late_cleaner_converges_on_a_cancellation_an_earlier_cleaner_committed_and_delivery_completed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var access = new FixedAccessor("tenant-a");
        await using var setup = database.Open();
        var scopes = new EfWorkflowTestScopeStore(setup, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(setup, access);
        var scope = Scope("cleanup-concurrent");
        await scopes.CreateAsync(scope, Now);
        var dispatch = Dispatch("concurrent", scope);
        await dispatches.SaveAsync(dispatch);
        var started = dispatch.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        var closingAt = (await scopes.FindAsync(scope.ScopeId))!.ClosingAt!.Value;
        var intents = new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = CancellationIntent(started, closingAt) };
        var outboxId = new WorkflowDispatchIdentity(
            started.ParentWorkflowExecutionId,
            started.ParentActivityExecutionId).ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");

        // The late cleaner has already selected the unmarked dispatch. Before its transaction begins, an earlier cleaner on
        // its own connection commits the cancellation and delivery claims and completes it.
        RuntimePostCommitOutboxItem? delivered = null;
        var interleaving = new BeforeFirstTransactionInterceptor(async () =>
        {
            await using var earlierContext = database.OpenOnOwnConnection();
            var earlier = await new EfWorkflowTestScopeCleanupStore(earlierContext, access, Codec())
                .CleanupAsync(scope, closingAt, 100, intents);
            Assert.Equal(1, earlier.CancellationQueued);
            var outbox = new EfRuntimePostCommitOutboxStore(earlierContext, access);
            var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "deliverer", closingAt.AddSeconds(1), TimeSpan.FromMinutes(5), 10, intentKind: CancelChildIntentKind)));
            Assert.Equal(outboxId, claim.OutboxItemId);
            await outbox.RecordDeliveryResultAsync(claim, new RuntimePostCommitOutboxDeliveryResult(
                outboxId, RuntimePostCommitOutboxStatus.Delivered, closingAt.AddSeconds(2)));
            delivered = await outbox.FindAsync(outboxId);
        });
        await using var lateContext = database.Open(interleaving);

        var late = await new EfWorkflowTestScopeCleanupStore(lateContext, access, Codec())
            .CleanupAsync(scope, closingAt, 100, intents);

        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered!.Status);
        Assert.Equal(1, late.Inspected);
        Assert.Equal(1, late.CancellationQueued);
        await using var verify = database.Open();
        var rows = await verify.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync();
        Assert.Equal(EfRelationalIdentity.Encode(outboxId), Assert.Single(rows).OutboxItemId);
        var after = (await new EfRuntimePostCommitOutboxStore(verify, access).FindAsync(outboxId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, after.Status);
        Assert.True(delivered.IsEquivalentTo(after), "The late cleaner must leave the delivered cancellation exactly as delivery left it.");
        var child = (await new EfWorkflowDispatchStore(verify, access).FindAsync(started.DispatchId))!;
        Assert.Equal(WorkflowDispatchStatus.Started, child.Status);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(child));
    }

    [Fact]
    public async Task Sqlite_missing_started_intent_rolls_back_every_participant()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-rollback");
        await scopes.CreateAsync(scope, Now);
        var pending = Dispatch("rollback-pending", scope);
        var started = Dispatch("rollback-started", scope, Now.AddSeconds(1));
        await dispatches.SaveAsync(pending);
        await dispatches.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>()).AsTask());

        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatches.FindAsync(started.DispatchId))!.Status);
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Sqlite_cleanup_rolls_back_provider_failure_without_later_sibling_flush()
    {
        var interceptor = new FailDispatchUpdateInterceptor();
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open(interceptor);
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-provider-failure");
        await scopes.CreateAsync(scope, Now);
        var pending = Dispatch("provider-failure", scope);
        await dispatches.SaveAsync(pending);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        interceptor.FailNextUpdate();

        await Assert.ThrowsAsync<DbUpdateException>(() => cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>()).AsTask());

        await context.SaveChangesAsync();
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Sqlite_cleanup_pages_by_creation_and_dispatch_identity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-page");
        await scopes.CreateAsync(scope, Now);
        foreach (var index in Enumerable.Range(0, 101))
            await dispatches.SaveAsync(Dispatch($"page-{index:D3}", scope, Now.AddTicks(index)));
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        var first = await cleanup.CleanupAsync(scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>());
        Assert.Equal(100, first.Inspected);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(1, first.RemainingLive);

        var second = await cleanup.CleanupAsync(scope, Now.AddMinutes(1), 100, new Dictionary<string, RuntimePostCommitIntent>(), first.ContinuationToken);
        Assert.Equal(1, second.Inspected);
        Assert.Null(second.ContinuationToken);
        Assert.Equal(0, second.RemainingLive);
    }

    [Fact]
    public async Task Sqlite_cleanup_preserves_full_ordinal_dispatch_order_for_same_timestamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopes = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, Codec());
        var scope = Scope("cleanup-tie-order");
        await scopes.CreateAsync(scope, Now);
        var first = Dispatch("tie-first", scope, Now);
        var second = Dispatch("tie-second", scope, Now);
        await dispatches.SaveAsync(first);
        await dispatches.SaveAsync(second);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        var expected = new[] { first.DispatchId, second.DispatchId }
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var firstPage = await cleanup.CleanupAsync(
            scope, Now.AddMinutes(1), 1, new Dictionary<string, RuntimePostCommitIntent>());
        Assert.Equal(1, firstPage.Inspected);
        Assert.NotNull(firstPage.ContinuationToken);
        var cancelledAfterFirstPage = await context.WorkflowDispatches.AsNoTracking()
            .Where(row => row.Status == (int)WorkflowDispatchStatus.Cancelled)
            .Select(row => row.DispatchId)
            .SingleAsync();
        Assert.Equal(EfRelationalIdentity.Encode(expected[0]), cancelledAfterFirstPage);

        var secondPage = await cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(1),
            1,
            new Dictionary<string, RuntimePostCommitIntent>(),
            firstPage.ContinuationToken);
        Assert.Equal(1, secondPage.Inspected);
        Assert.Null(secondPage.ContinuationToken);
        Assert.Equal(0, secondPage.RemainingLive);
    }

    private static WorkflowTestScope Scope(string id) =>
        new(id, Now.AddHours(1), "tenant-a", new WorkflowExecutionPartition("partition-a"));

    private static WorkflowDispatchRecord Dispatch(
        string id,
        WorkflowTestScope scope,
        DateTimeOffset? createdAt = null,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget)
    {
        var parent = $"parent-{id}";
        var activity = $"activity-{id}";
        var identity = new WorkflowDispatchIdentity(parent, activity);
        var timestamp = createdAt ?? Now;
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{id}", "definition-child", "version-child", "1", $"hash-{id}"),
            new WorkflowExecutableSourceProvenance($"source-{id}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            scope.TenantId,
            scope.Partition,
            WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [],
            timestamp,
            timestamp,
            new Dictionary<string, string>(),
            testScope: scope);
    }

    private static RuntimePostCommitIntent CancellationIntent(WorkflowDispatchRecord started, DateTimeOffset requestedAt)
    {
        var identity = new WorkflowDispatchIdentity(started.ParentWorkflowExecutionId, started.ParentActivityExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            started.ParentWorkflowExecutionId,
            CancelChildIntentKind,
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

    private static HmacRuntimeRecoveryContinuationCodec Codec() =>
        new(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TestDatabase(SqliteConnection keeper) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-test-scope-cleanup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var schema = new RuntimeSqliteDbContext(
                new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper).Options);
            await schema.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper);
        }

        public RuntimeSqliteDbContext Open(params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new RuntimeSqliteDbContext(builder.Options);
        }

        public RuntimeSqliteDbContext OpenOnOwnConnection() =>
            new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper.ConnectionString).Options);

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    /// <summary>Runs <paramref name="interleave"/> once, after the context's reads and before its first transaction begins.</summary>
    private sealed class BeforeFirstTransactionInterceptor(Func<Task> interleave) : DbTransactionInterceptor
    {
        private int ran;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref ran, 1) == 0)
                await interleave();
            return result;
        }
    }

    private sealed class FailDispatchUpdateInterceptor : DbCommandInterceptor
    {
        private int failNextUpdate;

        public void FailNextUpdate() => Interlocked.Exchange(ref failNextUpdate, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeWorkflowDispatchEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref failNextUpdate, 0) == 1)
                throw new DbUpdateException("Simulated dispatch update failure.");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeWorkflowDispatchEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref failNextUpdate, 0) == 1)
                throw new DbUpdateException("Simulated dispatch update failure.");
            return ValueTask.FromResult(result);
        }
    }
}
