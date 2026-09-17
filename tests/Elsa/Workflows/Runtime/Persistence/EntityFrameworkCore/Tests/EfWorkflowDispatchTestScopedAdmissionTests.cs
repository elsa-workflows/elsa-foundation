using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowDispatchTestScopedAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_open_test_scope_admission_starts_dispatch_and_touches_scope_atomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var testScope = Scope("scope-open");
        await scopeStore.CreateAsync(testScope, Now);
        var dispatch = Pending("parent-open", "activity-open", testScope);
        await dispatchStore.SaveAsync(dispatch);

        var admitted = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(1));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, admitted.Record.Status);
        Assert.Equal(WorkflowTestScopeState.Open, (await scopeStore.FindAsync(testScope.ScopeId))!.State);
        Assert.Equal(1, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());

        var replay = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(2));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, replay.Disposition);
        Assert.Equal(1, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_missing_scope_cancels_test_dispatch_before_admission_without_scope_write()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-missing", "activity-missing", Scope("scope-missing"));
        await dispatchStore.SaveAsync(dispatch);

        var result = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(1));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, result.Record.Status);
        Assert.Equal(WorkflowDispatchLifecycle.ScopeCancelledBeforeAdmissionState,
            result.Record.Metadata[WorkflowDispatchLifecycle.CancellationStateMetadataKey]);
        Assert.Empty(await context.WorkflowTestScopes.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Sqlite_closed_expired_and_mismatched_scopes_cancel_before_admission()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);

        var closedScope = Scope("scope-closed");
        await scopeStore.CreateAsync(closedScope, Now);
        await scopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(
            closedScope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));
        var closedDispatch = Pending("parent-closed", "activity-closed", closedScope);

        var expiredScope = Scope("scope-expired", Now.AddMinutes(1));
        await scopeStore.CreateAsync(expiredScope, Now);
        var expiredDispatch = Pending("parent-expired", "activity-expired", expiredScope);

        var actualContext = Scope("scope-mismatch", partition: "actual-partition");
        await scopeStore.CreateAsync(actualContext, Now);
        var mismatchedDispatch = Pending(
            "parent-mismatch", "activity-mismatch", Scope("scope-mismatch", partition: "dispatch-partition"));

        await dispatchStore.SaveAsync(closedDispatch);
        await dispatchStore.SaveAsync(expiredDispatch);
        await dispatchStore.SaveAsync(mismatchedDispatch);

        var closed = await dispatchStore.TryAdmitAsync(closedDispatch.DispatchId, Now.AddMinutes(2));
        var expired = await dispatchStore.TryAdmitAsync(expiredDispatch.DispatchId, expiredScope.ExpiresAt);
        var mismatched = await dispatchStore.TryAdmitAsync(mismatchedDispatch.DispatchId, Now.AddMinutes(1));

        Assert.All(new[] { closed, expired, mismatched }, result =>
        {
            Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, result.Disposition);
            Assert.Equal(WorkflowDispatchStatus.Cancelled, result.Record.Status);
        });
        Assert.Equal(1, await context.WorkflowTestScopes.AsNoTracking()
            .Where(row => row.ScopeId == EfRelationalIdentity.Encode(closedScope.ScopeId))
            .Select(row => row.Revision).SingleAsync());
        Assert.Equal(0, await context.WorkflowTestScopes.AsNoTracking()
            .Where(row => row.ScopeId == EfRelationalIdentity.Encode(expiredScope.ScopeId))
            .Select(row => row.Revision).SingleAsync());
        Assert.Equal(0, await context.WorkflowTestScopes.AsNoTracking()
            .Where(row => row.ScopeId == EfRelationalIdentity.Encode(actualContext.ScopeId))
            .Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_cancelled_test_dispatch_replays_without_touching_open_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var testScope = Scope("scope-cancelled");
        await scopeStore.CreateAsync(testScope, Now);
        var dispatch = Pending("parent-cancelled", "activity-cancelled", testScope);
        await dispatchStore.SaveAsync(dispatch);
        var cancelled = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(dispatch, Now.AddMinutes(1));
        await dispatchStore.SaveAsync(cancelled);

        var replay = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(2));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, replay.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, replay.Record.Status);
        Assert.Equal(0, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_scope_revision_race_retries_and_refuses_admission_after_close()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var testScope = Scope("scope-race");
        await scopeStore.CreateAsync(testScope, Now);
        var dispatch = Pending("parent-race", "activity-race", testScope);
        await dispatchStore.SaveAsync(dispatch);

        // The first context retains the open scope at revision zero. A second context closes it before the
        // first context's conditional scope touch, forcing the admission CAS to retry against the closing row.
        await using (var competingContext = database.Open())
        {
            var competingScopeStore = new EfWorkflowTestScopeStore(
                competingContext, new FixedAccessor("tenant-a"), Codec());
            Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted,
                (await competingScopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(
                    testScope.ScopeId,
                    WorkflowTestScopeCloseReason.ExplicitTeardown,
                    Now.AddMinutes(1)))).Disposition);
        }

        var result = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(2));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, result.Disposition);
        Assert.Equal(WorkflowTestScopeState.Closing, (await scopeStore.FindAsync(testScope.ScopeId))!.State);
        Assert.Equal(1, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_failed_scope_write_rolls_back_dispatch_and_preserves_sibling_tracking()
    {
        var interceptor = new FailTestScopeUpdateInterceptor();
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open(interceptor);
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var testScope = Scope("scope-rollback");
        await scopeStore.CreateAsync(testScope, Now);
        var dispatch = Pending("parent-rollback", "activity-rollback", testScope);
        var sibling = Pending("parent-sibling", "activity-sibling");
        await dispatchStore.SaveAsync(dispatch);
        await dispatchStore.SaveAsync(sibling);
        var siblingRow = await context.WorkflowDispatches.SingleAsync(row =>
            row.DispatchId == EfRelationalIdentity.Encode(sibling.DispatchId));
        siblingRow.Revision++;
        interceptor.FailNextUpdate();

        await Assert.ThrowsAsync<DbUpdateException>(() => dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(1)).AsTask());

        // The failed admission detached only its two participant rows. A later sibling flush is still allowed.
        await context.SaveChangesAsync();
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(0, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());
        Assert.Equal(2, await context.WorkflowDispatches.AsNoTracking()
            .Where(row => row.DispatchId == EfRelationalIdentity.Encode(sibling.DispatchId))
            .Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Cancellation_before_io_does_not_mutate_dispatch_or_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var scopeStore = new EfWorkflowTestScopeStore(context, access, Codec());
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var testScope = Scope("scope-cancel-token");
        await scopeStore.CreateAsync(testScope, Now);
        var dispatch = Pending("parent-cancel-token", "activity-cancel-token", testScope);
        await dispatchStore.SaveAsync(dispatch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(1), cancellation.Token).AsTask());
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(0, await context.WorkflowTestScopes.AsNoTracking().Select(row => row.Revision).SingleAsync());
    }

    private static WorkflowTestScope Scope(
        string id,
        DateTimeOffset? expiresAt = null,
        string partition = "partition-a") =>
        new(id, expiresAt ?? Now.AddHours(1), "tenant-a", new WorkflowExecutionPartition(partition));

    private static WorkflowDispatchRecord Pending(string parent, string activity, WorkflowTestScope? scope = null)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{activity}", "definition-child", "version-child", "1", $"hash-{activity}"),
            new WorkflowExecutableSourceProvenance($"source-{activity}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            scope?.TenantId ?? "tenant-a",
            scope?.Partition ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            scope is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" },
            testScope: scope);
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
            var connectionString = $"Data Source=file:test-scoped-admission-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var schema = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await schema.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper);
        }

        public BookmarkStateSqliteDbContext Open(params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new BookmarkStateSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class FailTestScopeUpdateInterceptor : DbCommandInterceptor
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
                command.CommandText.Contains(RuntimeWorkflowTestScopeEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref failNextUpdate, 0) == 1)
                throw new DbUpdateException("Simulated test-scope update failure.");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeWorkflowTestScopeEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref failNextUpdate, 0) == 1)
                throw new DbUpdateException("Simulated test-scope update failure.");
            return ValueTask.FromResult(result);
        }
    }
}
