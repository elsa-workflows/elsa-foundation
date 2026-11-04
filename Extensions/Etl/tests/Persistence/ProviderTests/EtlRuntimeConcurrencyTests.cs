using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Etl.Tests.EtlRuntimeStoreContract;
using Xunit.Abstractions;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>Overlapping runtime transactions on actual servers; injected failures are tested separately.</summary>
public abstract partial class EtlRuntimeConcurrencyTests(EtlNativeProviderFixture fixture, ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Enlisted_transaction_is_refused_without_ambient_scope_or_tracking_mutation()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var commands = new NativeCommandCounter();
        await using var context = database.CreateContext(commands);
        var store = ((IEtlRuntimeTestDatabase)database).CreateStore(context);
        var admission = EtlRuntimeFixtures.Admission("enlisted-owner", new EtlScope("tenant-race", "partition-race"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        var row = await context.Runs.SingleAsync();
        row.Revision++;
        Assert.True(context.ChangeTracker.HasChanges());
        await context.Database.OpenConnectionAsync();
        using var transaction = new CommittableTransaction();
        context.Database.EnlistTransaction(transaction);
        Assert.Null(Transaction.Current);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Same(transaction, context.Database.GetEnlistedTransaction());
        var before = commands.Commands;

        var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.AdmitAsync(admission));

        Assert.Equal(nameof(store.AdmitAsync), failure.Operation);
        Assert.Same(transaction, context.Database.GetEnlistedTransaction());
        Assert.Equal(before, commands.Commands);
        Assert.Equal(EntityState.Modified, context.Entry(row).State);
        transaction.Rollback();
        context.Database.EnlistTransaction(null);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_admission_retries_native_conflicts_as_a_whole(bool differentBindings)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var leftContext = database.CreateContext(new NativeRaceSave(gate, conflicts));
        await using var rightContext = database.CreateContext(new NativeRaceSave(gate, conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(leftContext);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(rightContext);
        var admission = EtlRuntimeFixtures.Admission("native-admission-race", new EtlScope("tenant-race", "partition-race"));
        var other = differentBindings ? admission with
        {
            Bindings = admission.Bindings with { Output = admission.Bindings.Output with { Revision = "other" } }
        } : admission;

        var first = leftStore.AdmitAsync(admission, cancellation.Token).AsTask();
        var second = rightStore.AdmitAsync(other, cancellation.Token).AsTask();
        await ObserveRaceAsync(Task.WhenAll(first, second), conflicts, cancellation);
        var results = new[] { await first, await second };

        Assert.Equal([EtlStoreStatus.Applied, differentBindings ? EtlStoreStatus.Conflict : EtlStoreStatus.Replay],
            results.Select(result => result.Status).Order().ToArray());
        Assert.NotEmpty(conflicts);
        await using var observer = database.CreateContext();
        Assert.Single(await observer.Runs.AsNoTracking().ToListAsync());
        Assert.Empty(await observer.Terminals.AsNoTracking().ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_preparers_retain_one_authority(bool connector)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var scope = new EtlScope("tenant-race", "preparation-authority");
        await using (var seed = database.CreateContext())
        {
            var store = ((IEtlRuntimeTestDatabase)database).CreateStore(seed, clock);
            if (connector)
                Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(EtlRuntimeFixtures.ConnectorAdmission("prepare", scope))).Status);
            else
                Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(EtlRuntimeFixtures.Admission("prepare", scope))).Status);
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock);
        var a = (connector ? leftStore.ClaimConnectorPreparationAsync(scope, "left", cancellation.Token) : leftStore.ClaimPreparationAsync(scope, "left", cancellation.Token)).AsTask();
        var b = (connector ? rightStore.ClaimConnectorPreparationAsync(scope, "right", cancellation.Token) : rightStore.ClaimPreparationAsync(scope, "right", cancellation.Token)).AsTask();

        await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);

        var authority = Assert.Single(new[] { await a, await b }, lease => lease is not null)!;
        await using var observer = database.CreateContext();
        var retained = await observer.Runs.AsNoTracking().SingleAsync();
        Assert.Equal(1, retained.PreparationFence);
        Assert.Equal(authority.Fence, retained.PreparationFence);
        Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity(authority.Owner, "worker id"), retained.PreparationOwner);
        Assert.Equal(authority.ExpiresAt.UtcTicks, retained.PreparationExpiresAtUtcTicks);
        Assert.Equal((int)EtlRunStatus.Preparing, retained.Status);
        Assert.Empty(await observer.Terminals.AsNoTracking().ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_partition_claims_retain_one_authority(bool connector)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var scope = new EtlScope("tenant-race", "partition-authority");
        await using (var seed = database.CreateContext())
        {
            var store = ((IEtlRuntimeTestDatabase)database).CreateStore(seed, clock);
            if (connector)
            {
                var admission = EtlRuntimeFixtures.ConnectorAdmission("partition", scope, httpShardIds: ["single"]);
                Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
                var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(scope, "prepare"));
                Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(preparation, new(null, null)));
            }
            else
            {
                var pipeline = EtlTestPipelines.Create() with { Limits = EtlTestPipelines.Create().Limits with { PartitionCount = 1 } };
                await StartAsync(store, CreateAdmission("partition", scope, pipeline), 2);
            }
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock);
        if (connector)
        {
            var a = leftStore.ClaimConnectorPartitionAsync(scope, "partition", "left", cancellation.Token).AsTask();
            var b = rightStore.ClaimConnectorPartitionAsync(scope, "partition", "right", cancellation.Token).AsTask();
            await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);
            var authority = Assert.Single(new[] { await a, await b }, lease => lease is not null)!;
            await using var observer = database.CreateContext();
            var retained = await observer.ConnectorPartitions.AsNoTracking().SingleAsync();
            Assert.Equal(1, retained.Fence);
            Assert.Equal(authority.Fence, retained.Fence);
            Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity(authority.Owner, "worker id"), retained.Owner);
            Assert.Equal(authority.ExpiresAt.UtcTicks, retained.ExpiresAtUtcTicks);
            Assert.Single(await observer.ConnectorHttpRequests.AsNoTracking().ToListAsync());
        }
        else
        {
            var a = leftStore.ClaimPartitionAsync(scope, "partition", "left", cancellation.Token).AsTask();
            var b = rightStore.ClaimPartitionAsync(scope, "partition", "right", cancellation.Token).AsTask();
            await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);
            var authority = Assert.Single(new[] { await a, await b }, lease => lease is not null)!;
            await using var observer = database.CreateContext();
            var retained = await observer.Partitions.AsNoTracking().SingleAsync();
            Assert.Equal(1, retained.Fence);
            Assert.Equal(authority.Fence, retained.Fence);
            Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity(authority.Owner, "worker id"), retained.Owner);
            Assert.Equal(authority.ExpiresAt.UtcTicks, retained.ExpiresAtUtcTicks);
        }
    }

    [SkippableFact]
    public async Task Competing_reservations_cannot_exceed_the_run_budget()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = EtlTestPipelines.Create().Limits with
            {
                PartitionCount = 2, MaxBatchRecords = 2, MaxOutstandingRecords = 2,
                MaxRecordBytes = 1024, MaxBatchBytes = 1024, MaxOutstandingBytes = 1024
            }
        };
        var admission = CreateAdmission("budget", new EtlScope("tenant-race", "budget"), pipeline);
        EtlPartitionLease first, second;
        await using (var seed = database.CreateContext())
        {
            var store = ((IEtlRuntimeTestDatabase)database).CreateStore(seed, clock);
            await StartAsync(store, admission, 4);
            first = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, "budget", "first"));
            second = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, "budget", "second"));
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock);
        var a = leftStore.ReserveBatchAsync(first, CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), first.PartitionOrdinal, first.StartOrdinal, first.EndOrdinalExclusive, "first"), cancellation.Token).AsTask();
        var b = rightStore.ReserveBatchAsync(second, CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), second.PartitionOrdinal, second.StartOrdinal, second.EndOrdinalExclusive, "second"), cancellation.Token).AsTask();

        await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);

        Assert.Equal([EtlStoreStatus.Applied, EtlStoreStatus.BudgetExceeded], new[] { (await a).Status, (await b).Status }.Order().ToArray());
        await using var observer = database.CreateContext();
        var run = await observer.Runs.AsNoTracking().SingleAsync();
        Assert.Equal(2, run.OutstandingRecords);
        Assert.Equal(100, run.OutstandingBytes);
        Assert.Single(await observer.Reservations.AsNoTracking().ToListAsync());
        Assert.Empty(await observer.Manifest.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task Competing_final_acceptance_and_cancellation_retain_one_terminal_decision()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var pipeline = EtlTestPipelines.Create() with { Limits = EtlTestPipelines.Create().Limits with { PartitionCount = 1 } };
        var admission = CreateAdmission("final", new EtlScope("tenant-race", "final"), pipeline);
        var (lease, acceptance) = await SeedFinalBatchAsync(database, admission, clock);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        var a = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock).AcceptBatchAsync(lease, acceptance, cancellation.Token).AsTask();
        var b = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock).CancelAsync(admission.Invocation, cancellation.Token).AsTask();

        await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);

        await AssertFinalBatchRaceStateAsync(database, admission, acceptance, await a, await b, (await a).Status == EtlStoreStatus.Applied, clock);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_delivery_transitions_retain_one_claim_and_acknowledgement(bool acknowledge)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var admission = EtlRuntimeFixtures.Admission("delivery", new EtlScope("tenant-race", "delivery"));
        EtlDeliveryLease? delivery = null;
        await using (var seed = database.CreateContext())
        {
            var store = ((IEtlRuntimeTestDatabase)database).CreateStore(seed, clock);
            Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(admission.Invocation)).Status);
            if (acknowledge)
                delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(admission.Invocation.Scope, "delivery"));
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new NativeRaceSave(gate, conflicts), new NativeRaceCommand(conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock);
        if (acknowledge)
        {
            var a = leftStore.AcknowledgeDeliveryAsync(delivery!, EtlDeliveryDisposition.Consumed, cancellation.Token).AsTask();
            var b = rightStore.AcknowledgeDeliveryAsync(delivery!, EtlDeliveryDisposition.Consumed, cancellation.Token).AsTask();
            await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);
            Assert.Equal([EtlStoreStatus.Applied, EtlStoreStatus.Replay], new[] { await a, await b }.Order().ToArray());
        }
        else
        {
            var a = leftStore.ClaimDeliveryAsync(admission.Invocation.Scope, "left", cancellation.Token).AsTask();
            var b = rightStore.ClaimDeliveryAsync(admission.Invocation.Scope, "right", cancellation.Token).AsTask();
            await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);
            delivery = Assert.Single(new[] { await a, await b }, lease => lease is not null)!;
        }
        await using var observer = database.CreateContext();
        var retained = await observer.Outbox.AsNoTracking().SingleAsync();
        Assert.Equal(delivery!.DeliveryId, retained.DeliveryId);
        Assert.Equal(delivery.Fence, retained.Fence);
        Assert.Equal(acknowledge ? (int?)EtlDeliveryDisposition.Consumed : null, retained.Disposition);
        if (acknowledge)
        {
            Assert.Null(retained.Owner);
            Assert.Null(retained.ExpiresAtUtcTicks);
            Assert.NotNull(retained.AcknowledgedAtUtcTicks);
        }
        else
        {
            Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity(delivery.Owner, "worker id"), retained.Owner);
            Assert.Equal(delivery.ExpiresAt.UtcTicks, retained.ExpiresAtUtcTicks);
        }
        Assert.Single(await observer.Terminals.AsNoTracking().ToListAsync());
    }

    private async Task ObserveRaceAsync(Task race, ConcurrentQueue<string> conflicts, CancellationTokenSource cancellation)
    {
        try
        {
            await race.WaitAsync(cancellation.Token);
        }
        catch
        {
            await cancellation.CancelAsync();
            try { await race; }
            catch { /* Preserve the original failure after both participants have terminated. */ }
            throw;
        }
        finally
        {
            foreach (var conflict in conflicts.Distinct())
                output.WriteLine("Observed native conflict: " + conflict);
        }
        Assert.NotEmpty(conflicts);
    }

    private sealed class FirstSaveGate
    {
        private int arrivals;
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                released.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }

    private sealed class NativeCommandCounter : DbCommandInterceptor
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

    private sealed class NativeRaceSave(FirstSaveGate gate, ConcurrentQueue<string> conflicts) : SaveChangesInterceptor
    {
        private int joined;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref joined, 1) == 0)
                await gate.ArriveAsync(cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            ObserveNativeFailure(eventData.Exception, conflicts);
            return Task.CompletedTask;
        }
    }

    private sealed class NativeRaceCommand(ConcurrentQueue<string> conflicts) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ObserveNativeFailure(eventData.Exception, conflicts);
            return Task.CompletedTask;
        }
    }

    private static void ObserveNativeFailure(Exception failure, ConcurrentQueue<string> conflicts)
    {
        if (!EfRelationalExceptionClassifier.IsSaveConflict(failure, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey) &&
            !EfRelationalExceptionClassifier.IsTransientWriteConflict(failure)) return;
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is not DbException) continue;
            var type = cause.GetType();
            var code = type.GetProperty("SqlState")?.GetValue(cause) ?? type.GetProperty("Number")?.GetValue(cause);
            conflicts.Enqueue(type.Name + ":" + code);
            break;
        }
    }
}

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencyPostgreSqlTests(EtlPostgreSqlProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencySqlServerTests(EtlSqlServerProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencyMySqlTests(EtlMySqlProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);
