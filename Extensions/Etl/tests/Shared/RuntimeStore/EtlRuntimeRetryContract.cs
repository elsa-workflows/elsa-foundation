using System.Data.Common;
using System.Transactions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Persistence.EntityFramework;
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

    private sealed class RuntimeSaveFault(int failures, Exception? failure = null) : SaveChangesInterceptor
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
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }
    }
}
