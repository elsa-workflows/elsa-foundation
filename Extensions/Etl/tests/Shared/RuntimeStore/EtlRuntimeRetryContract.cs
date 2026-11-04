using System.Data.Common;
using System.Transactions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

public abstract partial class EtlRuntimeStoreContract
{
    [SkippableFact]
    public async Task Admission_retries_the_complete_transaction_after_a_failed_save()
    {
        await using var database = await CreateDatabaseAsync();
        var fault = new RuntimeSaveFault(failures: 1);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("retry-admission", new EtlScope("tenant-retry", "partition-retry"));

        var admitted = await store.AdmitAsync(admission);

        Assert.Equal(EtlStoreStatus.Applied, admitted.Status);
        Assert.Equal(2, fault.Attempts);
        Assert.Equal(admission.Invocation.RunId, admitted.Run!.Invocation.RunId);
        Assert.Single(await context.Runs.AsNoTracking().ToListAsync());
        Assert.Equal(EtlStoreStatus.Replay, (await store.AdmitAsync(admission)).Status);
    }

    [SkippableFact]
    public async Task Admission_exhaustion_preserves_the_original_cause_and_abandons_pending_inserts()
    {
        await using var database = await CreateDatabaseAsync();
        var fault = new RuntimeSaveFault(int.MaxValue);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("retry-exhaustion", new EtlScope("tenant-retry", "partition-retry"));

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.AdmitAsync(admission));

        Assert.Equal(nameof(store.AdmitAsync), failure.Operation);
        Assert.Equal(admission.Invocation.Scope, failure.Scope);
        Assert.Equal(admission.Invocation.RunId, failure.RunId);
        Assert.Same(fault.Failure, failure.InnerException);
        Assert.Equal(EfWriteRetry.DefaultMaxAttempts, fault.Attempts);
        Assert.Empty(context.ChangeTracker.Entries());
        await using (var observer = database.CreateContext())
            Assert.Empty(await observer.Runs.AsNoTracking().ToListAsync());

        fault.RemainingFailures = 0;
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        Assert.Single(await context.Runs.AsNoTracking().ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_nonretryable_provider_failure_is_wrapped_once_and_cleans_tracking(bool wrapped)
    {
        await using var database = await CreateDatabaseAsync();
        var providerFailure = new ControlledRuntimeDbFailure();
        Exception injected = wrapped ? new InvalidOperationException("Controlled provider wrapper.", providerFailure) : providerFailure;
        var fault = new RuntimeSaveFault(1, injected);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("retry-nontransient", new EtlScope("tenant-retry", "partition-retry"));

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.AdmitAsync(admission));

        Assert.Equal(nameof(store.AdmitAsync), failure.Operation);
        Assert.Same(injected, failure.InnerException);
        Assert.Equal(1, fault.Attempts);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var observer = database.CreateContext();
        Assert.Empty(await observer.Runs.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task Admission_refuses_a_caller_transaction_before_discarding_its_pending_tracking()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("caller-transaction", new EtlScope("tenant-retry", "partition-retry"));
        await store.AdmitAsync(admission);
        var retained = await context.Runs.SingleAsync();
        retained.Revision++;
        Assert.True(context.ChangeTracker.HasChanges());
        await using var transaction = await context.Database.BeginTransactionAsync();

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.AdmitAsync(admission));

        Assert.Equal(nameof(store.AdmitAsync), failure.Operation);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(EntityState.Modified, context.Entry(retained).State);
        Assert.True(context.ChangeTracker.HasChanges());
        await transaction.RollbackAsync();
    }

    [SkippableFact]
    public async Task Admission_refuses_an_ambient_transaction_before_issuing_a_database_command()
    {
        await using var database = await CreateDatabaseAsync();
        var commands = new RuntimeCommandCounter();
        await using var context = database.CreateContext(commands);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("ambient-transaction", new EtlScope("tenant-retry", "partition-retry"));
        await store.AdmitAsync(admission);
        var retained = await context.Runs.SingleAsync();
        retained.Revision++;
        Assert.True(context.ChangeTracker.HasChanges());
        var before = commands.Commands;
        using var transaction = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled);
        var ambient = Transaction.Current;

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.AdmitAsync(admission));

        Assert.Equal(nameof(store.AdmitAsync), failure.Operation);
        Assert.Same(ambient, Transaction.Current);
        Assert.Equal(before, commands.Commands);
        Assert.Equal(EntityState.Modified, context.Entry(retained).State);
    }

    [SkippableFact]
    public async Task Admission_cancellation_is_not_normalized_or_retried()
    {
        await using var database = await CreateDatabaseAsync();
        var fault = new RuntimeSaveFault(1);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("retry-cancelled", new EtlScope("tenant-retry", "partition-retry"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.AdmitAsync(admission, cancellation.Token));

        Assert.Equal(0, fault.Attempts);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SkippableFact]
    public async Task Cancellation_between_attempts_abandons_the_operation_without_another_save()
    {
        await using var database = await CreateDatabaseAsync();
        using var cancellation = new CancellationTokenSource();
        var fault = new RuntimeSaveFault(int.MaxValue, onFailure: cancellation.Cancel);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("retry-cancellation", new EtlScope("tenant-retry", "partition-retry"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.AdmitAsync(admission, cancellation.Token));

        Assert.Equal(1, fault.Attempts);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var observer = database.CreateContext();
        Assert.Empty(await observer.Runs.AsNoTracking().ToListAsync());
    }

    [SkippableTheory]
    [InlineData("runtime", false)]
    [InlineData("runtime", true)]
    [InlineData("connector", false)]
    [InlineData("connector", true)]
    [InlineData("inspection", false)]
    [InlineData("inspection", true)]
    public async Task All_runtime_ports_exhaust_one_budget_then_read_the_retained_run(string port, bool wrapped)
    {
        await using var database = await CreateDatabaseAsync();
        var conflict = new DbUpdateConcurrencyException("Controlled query-boundary conflict.");
        Exception injected = wrapped ? new InvalidOperationException("Controlled query wrapper.", conflict) : conflict;
        var fault = new RuntimeReadFault(injected) { Armed = false };
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("read-retry-budget", new EtlScope("tenant-read", "partition-read"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        fault.Armed = true;
        var operation = port switch
        {
            "runtime" => nameof(IEtlRuntimeStore.FindRunAsync),
            "connector" => nameof(IEtlConnectorRuntimeStore.FindConnectorRunAsync),
            _ => nameof(IEtlConnectorRuntimeInspector.FindConnectorInspectionAsync)
        };

        async Task<EtlInvocation?> ReadAsync()
        {
            var scope = admission.Invocation.Scope;
            var runId = admission.Invocation.RunId;
            if (port == "runtime") return (await ((IEtlRuntimeStore)store).FindRunAsync(scope, runId))?.Invocation;
            if (port == "connector") return (await ((IEtlConnectorRuntimeStore)store).FindConnectorRunAsync(scope, runId))?.Run.Invocation;
            return (await ((IEtlConnectorRuntimeInspector)store).FindConnectorInspectionAsync(scope, runId))?.Run.Run.Invocation;
        }

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await ReadAsync());

        Assert.Equal(operation, failure.Operation);
        Assert.Equal(admission.Invocation.Scope, failure.Scope);
        Assert.Equal(admission.Invocation.RunId, failure.RunId);
        Assert.Same(injected, failure.InnerException);
        Assert.Equal(EfWriteRetry.DefaultMaxAttempts, fault.Failures);
        Assert.Empty(context.ChangeTracker.Entries());
        fault.Armed = false;
        var retained = Assert.IsType<EtlInvocation>(await ReadAsync());
        Assert.Equal(admission.Invocation.Scope, retained.Scope);
        Assert.Equal(admission.Invocation.RunId, retained.RunId);
        Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation), EtlRuntimeIdentity.InvocationFingerprint(retained));
    }

    [SkippableTheory]
    [InlineData("runtime", false)]
    [InlineData("runtime", true)]
    [InlineData("connector", false)]
    [InlineData("connector", true)]
    [InlineData("inspection", false)]
    [InlineData("inspection", true)]
    public async Task All_runtime_ports_wrap_read_failures_once_and_allow_reuse(string port, bool wrapped)
    {
        await using var database = await CreateDatabaseAsync();
        var cause = new ControlledRuntimeDbFailure();
        Exception injected = wrapped ? new InvalidOperationException("Controlled query wrapper.", cause) : cause;
        var fault = new RuntimeReadFault(injected);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var scope = new EtlScope("tenant-read", "partition-read");
        const string runId = "read-failure";
        var operation = port switch
        {
            "runtime" => nameof(IEtlRuntimeStore.FindRunAsync),
            "connector" => nameof(IEtlConnectorRuntimeStore.FindConnectorRunAsync),
            _ => nameof(IEtlConnectorRuntimeInspector.FindConnectorInspectionAsync)
        };

        async Task ReadAsync()
        {
            if (port == "runtime") await ((IEtlRuntimeStore)store).FindRunAsync(scope, runId);
            else if (port == "connector") await ((IEtlConnectorRuntimeStore)store).FindConnectorRunAsync(scope, runId);
            else await ((IEtlConnectorRuntimeInspector)store).FindConnectorInspectionAsync(scope, runId);
        }

        var failure = await Assert.ThrowsAsync<EtlStoreException>(ReadAsync);

        Assert.Equal(operation, failure.Operation);
        Assert.Equal(scope, failure.Scope);
        Assert.Equal(runId, failure.RunId);
        Assert.Same(injected, failure.InnerException);
        Assert.Equal(1, fault.Failures);
        Assert.Empty(context.ChangeTracker.Entries());
        fault.Armed = false;
        await ReadAsync();
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_preserves_schema_refusal_and_corruption_without_retry(bool schemaRefusal)
    {
        await using var database = await CreateDatabaseAsync();
        Exception injected = schemaRefusal ? new ControlledRuntimeSchemaRefusal() : new InvalidDataException("Controlled corrupt state.");
        var fault = new RuntimeSaveFault(1, injected);
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("distinct-refusal", new EtlScope("tenant-retry", "partition-retry"));

        var failure = await Record.ExceptionAsync(async () => await store.AdmitAsync(admission));

        Assert.Same(injected, failure);
        Assert.Equal(1, fault.Attempts);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var observer = database.CreateContext();
        Assert.Empty(await observer.Runs.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task Connector_admission_retains_the_first_pin_snapshot_across_retry()
    {
        await using var database = await CreateDatabaseAsync();
        var shards = new List<string> { "shard-a", "shard-b" };
        var fault = new RuntimeSaveFault(1, onFailure: () => shards[0] = "changed-between-attempts");
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("retry-pins", new EtlScope("tenant-retry", "partition-retry"), httpShardIds: shards);

        var result = await store.AdmitConnectorAsync(admission);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(2, fault.Attempts);
        Assert.Equal("changed-between-attempts", shards[0]);
        Assert.Equal(new[] { "shard-a", "shard-b" }, result.Run!.Bindings.HttpShardIds);
        var retained = await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(new[] { "shard-a", "shard-b" }, retained!.Bindings.HttpShardIds);
    }

    [SkippableFact]
    public async Task Acceptance_retains_the_first_effect_snapshot_across_retry()
    {
        await using var database = await CreateDatabaseAsync();
        EtlOutputEffect[] effects = [];
        var fault = new RuntimeSaveFault(0, onFailure: () =>
        {
            var index = Array.FindIndex(effects, effect => effect.Route == EtlOutputRoute.Error);
            effects[index] = effects[index] with { Chunk = effects[index].Chunk with { Key = "changed-between-attempts" } };
        });
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context, new MutableTimeProvider());
        var pipeline = EtlTestPipelines.Create() with { Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 2 } };
        var admission = CreateAdmission("retry-effects", new EtlScope("tenant-retry", "partition-retry"), pipeline);
        await StartAsync(store, admission, 2);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(2), lease.PartitionOrdinal, lease.StartOrdinal, lease.StartOrdinal + 2, "retry-effects");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, batch)).Status);
        var acceptance = CreateAcceptance(admission, batch, 1, 1, 0);
        effects = acceptance.Effects.ToArray();
        var expectedErrorKey = effects.Single(effect => effect.Route == EtlOutputRoute.Error).Chunk.Key;
        var before = fault.Attempts;
        fault.RemainingFailures = 1;

        var result = await store.AcceptBatchAsync(lease, acceptance with { Effects = effects });

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(before + 2, fault.Attempts);
        Assert.Equal("changed-between-attempts", effects.Single(effect => effect.Route == EtlOutputRoute.Error).Chunk.Key);
        var manifest = await store.ListManifestAsync(admission.Invocation.Scope, admission.Invocation.RunId, null, 10);
        Assert.Equal(expectedErrorKey, Assert.Single(manifest, item => item.Position.Route == EtlOutputRoute.Error).Effect.Chunk.Key);
        Assert.Single(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await context.Outbox.AsNoTracking().ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_retries_or_rolls_back_the_graph_after_a_successful_first_save(bool exhaust)
    {
        await using var database = await CreateDatabaseAsync();
        var fault = new RuntimePreparationSavedFault();
        await using var context = database.CreateContext(fault);
        var store = database.CreateStore(context, new MutableTimeProvider());
        var admission = ConnectorAdmission("retry-preparation", new EtlScope("tenant-retry", "partition-retry"), httpShardIds: ["shard-a", "shard-b"]);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var lease = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer"));
        fault.RemainingFailures = exhaust ? int.MaxValue : 1;

        if (exhaust)
        {
            var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.CommitConnectorPreparationAsync(lease, new(null, null)));
            Assert.Equal(nameof(store.CommitConnectorPreparationAsync), failure.Operation);
            Assert.Equal(lease.Scope, failure.Scope);
            Assert.Equal(lease.RunId, failure.RunId);
            Assert.Same(fault.Failure, failure.InnerException);
            Assert.Equal(EfWriteRetry.DefaultMaxAttempts, fault.PartialSavesObserved);
            Assert.Empty(context.ChangeTracker.Entries());
            await using (var observer = database.CreateContext())
            {
                Assert.Empty(await observer.ConnectorPartitions.AsNoTracking().ToListAsync());
                Assert.Empty(await observer.ConnectorHttpRequests.AsNoTracking().ToListAsync());
                Assert.Null((await observer.ConnectorRuns.AsNoTracking().SingleAsync()).PreparedJson);
                Assert.Equal((int)EtlRunStatus.Preparing, (await observer.Runs.AsNoTracking().SingleAsync()).Status);
            }
            fault.RemainingFailures = 0;
            Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(lease, new(null, null)));
        }
        else
        {
            Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(lease, new(null, null)));
            Assert.Equal(1, fault.PartialSavesObserved);
        }

        await using var retained = database.CreateContext();
        var partitions = await retained.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToListAsync();
        var requests = await retained.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToListAsync();
        Assert.Equal(2, partitions.Count);
        Assert.Equal(2, requests.Count);
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(index, partitions[index].PartitionOrdinal);
            Assert.Equal(index, requests[index].PartitionOrdinal);
            Assert.Equal(requests[index].PageSequence, partitions[index].CurrentRequestSequence);
            Assert.Equal(requests[index].RequestHash, partitions[index].CurrentRequestHash);
        }
        Assert.NotNull((await retained.ConnectorRuns.AsNoTracking().SingleAsync()).PreparedJson);
        Assert.Equal((int)EtlRunStatus.Running, (await retained.Runs.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await retained.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await retained.Outbox.AsNoTracking().ToListAsync());
    }

    private sealed class RuntimePreparationSavedFault : SaveChangesInterceptor
    {
        public int RemainingFailures { get; set; }
        public int PartialSavesObserved { get; private set; }
        public Exception Failure { get; } = new DbUpdateConcurrencyException("Controlled failure after the first preparation save.");

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (RemainingFailures <= 0) return result;
            var context = Assert.IsAssignableFrom<EtlRuntimeDbContext>(eventData.Context);
            Assert.NotNull(context.Database.CurrentTransaction);
            // The first save really reached the engine, but the request graph has not been saved yet.
            Assert.Equal(2, await context.ConnectorPartitions.AsNoTracking().CountAsync(cancellationToken));
            Assert.Empty(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync(cancellationToken));
            Assert.NotNull((await context.ConnectorRuns.AsNoTracking().SingleAsync(cancellationToken)).PreparedJson);
            PartialSavesObserved++;
            RemainingFailures--;
            throw Failure;
        }
    }

    private sealed class RuntimeReadFault(Exception failure) : DbCommandInterceptor
    {
        public bool Armed { get; set; } = true;
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Failures++;
            throw failure;
        }
    }

    private sealed class ControlledRuntimeSchemaRefusal()
        : SchemaWriteRefusedException("etl-runtime", "1", "2", "Controlled schema refusal.");

    private sealed class RuntimeCommandCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ControlledRuntimeDbFailure() : DbException("Controlled nonretryable ETL provider failure.");

    private sealed class RuntimeSaveFault(int failures, Exception? failure = null, Action? onFailure = null) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public Exception Failure { get; } = failure ?? new DbUpdateConcurrencyException("Controlled ETL save conflict.");
        public int RemainingFailures { get; set; } = failures;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (RemainingFailures > 0)
            {
                RemainingFailures--;
                onFailure?.Invoke();
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }
    }
}
