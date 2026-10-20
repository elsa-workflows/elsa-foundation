using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>Coordinates ETL-owned transitions; file work never runs inside a store transaction.</summary>
internal sealed class EtlExecution(
    IEtlRuntimeStore store,
    HostFileBindings bindings,
    EtlRuntimeOptions options,
    EtlExecutableReader executableReader,
    EtlAttemptBudget budget,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<EtlExecution> logger) : IEtlRunAdmissionService
{
    public async ValueTask<EtlAdmissionResult> AdmitAsync(EtlInvocation invocation, CancellationToken cancellationToken = default)
    {
        var existing = await store.FindRunAsync(invocation.Scope, invocation.RunId, cancellationToken);
        if (existing is not null)
            return Existing(invocation, existing);

        EtlRunAdmission admission;
        try
        {
            var pipeline = executableReader.Read(invocation.Artifact);
            var scope = invocation.Scope;
            admission = new(invocation, new(
                EtlRuntimeMetadata.Binding(bindings.Resolve(scope, pipeline.Bindings.Source, EtlFileRole.Source)),
                EtlRuntimeMetadata.Binding(bindings.Resolve(scope, pipeline.Bindings.Lookup, EtlFileRole.Lookup)),
                EtlRuntimeMetadata.Binding(bindings.Resolve(scope, pipeline.Bindings.Output, EtlFileRole.MainOutput)),
                EtlRuntimeMetadata.Binding(bindings.Resolve(scope, pipeline.Bindings.Errors, EtlFileRole.ErrorOutput))));
        }
        catch (EtlExecutableException exception)
        {
            return await RefuseAsync(invocation, exception.Code, cancellationToken);
        }
        catch (EtlBindingException exception)
        {
            return await RefuseAsync(invocation, exception.Code, cancellationToken);
        }

        var result = await store.AdmitAsync(admission, cancellationToken);
        if (result.Status != EtlStoreStatus.Conflict)
            return result;
        // Another admission may have won with an earlier host revision. Reuse its pins, never retarget them.
        existing = await store.FindRunAsync(invocation.Scope, invocation.RunId, cancellationToken);
        return existing is null ? result : Existing(invocation, existing);
    }

    public async ValueTask PrepareAsync(EtlPreparationLease lease, CancellationToken cancellationToken = default)
    {
        var heartbeat = Heartbeat((runtime, token) => runtime.RenewPreparationAsync(lease, token), cancellationToken);
        var token = heartbeat.Token;
        IAsyncDisposable? permit = null;
        try
        {
            var run = await store.FindRunAsync(lease.Scope, lease.RunId, token);
            if (run is not { Status: EtlRunStatus.Preparing, Bindings: not null })
                return;
            var pipeline = executableReader.Read(run.Invocation.Artifact);
            permit = await budget.AcquireAsync(Charge(pipeline.Limits), token);
            var preparer = new EtlFilePreparer(bindings, options.SnapshotRoot);
            var snapshots = await preparer.CreateSnapshotsAsync(lease.Scope, lease.RunId, pipeline,
                EtlRuntimeMetadata.PinnedBinding(lease.Scope, run.Bindings.Source, EtlFileRole.Source),
                EtlRuntimeMetadata.PinnedBinding(lease.Scope, run.Bindings.Lookup, EtlFileRole.Lookup), token);
            if (!snapshots.Succeeded)
            {
                await SettleAsync(lease, snapshots.Diagnostic!, token);
                return;
            }
            var preparation = await preparer.PrepareAsync(pipeline, snapshots.Snapshots!, token);
            if (!preparation.Succeeded)
            {
                await SettleAsync(lease, preparation.Diagnostic!, token);
                return;
            }
            var committed = await store.CommitPreparationAsync(lease, EtlRuntimeMetadata.Prepared(preparation.Prepared!), token);
            if (committed is EtlStoreStatus.Invalid or EtlStoreStatus.BudgetExceeded)
                await SettleAsync(lease, EtlPreparationDiagnostic.Fault("etl.preparation-store-refused",
                    new InvalidDataException("The verified preparation was refused by the ETL runtime store.")), token);
        }
        catch (EtlExecutableException exception)
        {
            await SettleAsync(lease, new(exception.Code), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutdown, cancellation or claim loss leaves durable responsibility intact.
        }
        finally
        {
            await using var retainedPermit = permit;
            await heartbeat.DisposeAsync();
        }
    }

    public async ValueTask ExecutePartitionAsync(EtlPartitionLease initialLease, CancellationToken cancellationToken = default)
    {
        var lease = initialLease;
        var heartbeat = Heartbeat((runtime, token) => runtime.RenewPartitionAsync(Volatile.Read(ref lease), token), cancellationToken);
        var token = heartbeat.Token;
        IAsyncDisposable? permit = null;
        try
        {
            var run = await store.FindRunAsync(lease.Scope, lease.RunId, token);
            if (run is not { Status: EtlRunStatus.Running, Bindings: not null, Prepared: not null })
                return;
            var pipeline = executableReader.Read(run.Invocation.Artifact);
            permit = await budget.AcquireAsync(Charge(pipeline.Limits), token);
            var admission = new EtlRunAdmission(run.Invocation, run.Bindings);
            var snapshots = new EtlInputSnapshots(lease.Scope, lease.RunId,
                EtlRuntimeMetadata.PinnedBinding(lease.Scope, run.Bindings.Source, EtlFileRole.Source),
                EtlRuntimeMetadata.PinnedBinding(lease.Scope, run.Bindings.Lookup, EtlFileRole.Lookup),
                Chunk(run.Prepared.Source), Chunk(run.Prepared.Lookup));
            var preparation = await new EtlFilePreparer(bindings, options.SnapshotRoot).PrepareAsync(pipeline, snapshots, token);
            if (!preparation.Succeeded)
            {
                await SettleAsync(lease, preparation.Diagnostic!, token);
                return;
            }
            var prepared = preparation.Prepared!;
            if (EtlRuntimeMetadata.Prepared(prepared) != run.Prepared || lease.PartitionOrdinal < 0 ||
                lease.PartitionOrdinal >= prepared.Partitions.Count ||
                prepared.Partitions[lease.PartitionOrdinal] != new EtlPartitionRange(lease.StartOrdinal, lease.EndOrdinalExclusive))
            {
                await SettleAsync(lease, new("etl.retained-preparation-mismatch"), token);
                return;
            }

            await using var builder = await EtlInputBatchBuilder.OpenAsync(options.SnapshotRoot, pipeline, prepared, token);
            while (lease.NextOrdinal < lease.EndOrdinalExclusive)
            {
                token.ThrowIfCancellationRequested();
                var partitions = await store.ListPartitionsAsync(lease.Scope, lease.RunId, lease.PartitionOrdinal - 1, 1, token);
                var partition = partitions.SingleOrDefault();
                if (partition?.PartitionOrdinal != lease.PartitionOrdinal || partition.Fence != lease.Fence || partition.Owner != lease.Owner)
                    return;
                var owed = partition.OwedBatch;
                var built = owed is null
                    ? await builder.BuildNextAsync(lease.PartitionOrdinal, lease.NextOrdinal, token)
                    : await builder.RebuildAsync(lease.PartitionOrdinal, owed.Batch.StartOrdinal, owed.Batch.EndOrdinalExclusive,
                        owed.Batch.InputBytes, owed.Batch.InputHash, token);
                if (!built.Succeeded || built.Batch is null)
                {
                    await SettleAsync(lease, built.Diagnostic ?? new("etl.batch-cursor-mismatch"), token);
                    return;
                }
                var descriptor = EtlRuntimeMetadata.Batch(admission, prepared, built.Batch);
                var reservation = await store.ReserveBatchAsync(lease, descriptor, token);
                if (reservation.Status == EtlStoreStatus.BudgetExceeded)
                    return; // Yield after unwind so already-owed work can free durable quota.
                if (reservation.Status is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal)
                    return;
                if (reservation.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay) ||
                    reservation.Reservation is not { Settled: false } || reservation.Reservation.Batch != descriptor)
                {
                    await SettleAsync(lease, EtlPreparationDiagnostic.Fault("etl.batch-reservation-conflict",
                        new InvalidDataException("The owed ETL descriptor does not match its immutable input.")), token);
                    return;
                }
                if (await store.RenewPartitionAsync(lease, token) is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                    return;
                var processed = await EtlBatchProcessor.ProcessAsync(admission, pipeline, prepared, built.Batch, bindings, token);
                if (!processed.Succeeded)
                {
                    await SettleAsync(lease, processed.Diagnostic!, token);
                    return;
                }
                var accepted = await store.AcceptBatchAsync(lease, processed.Acceptance!, token);
                if (accepted.Status is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal)
                    return;
                if (accepted.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                {
                    await SettleAsync(lease, EtlPreparationDiagnostic.Fault("etl.batch-acceptance-conflict",
                        new InvalidDataException("The ETL acceptance did not match its owed descriptor.")), token);
                    return;
                }
                if (accepted.Lease is null)
                    return;
                Volatile.Write(ref lease, accepted.Lease);
            }
        }
        catch (EtlExecutableException exception)
        {
            await SettleAsync(lease, new(exception.Code), token);
        }
        catch (EtlInputBatchBuilderException exception)
        {
            await SettleAsync(lease, exception.Diagnostic, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            // Keep this live attempt's allowances through reader/writer and asynchronous claim cleanup.
            // A replacement retains the durable reservation but must acquire its own local permit.
            await using var retainedPermit = permit;
            await heartbeat.DisposeAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
            try { await store.ReleasePartitionAsync(lease, cleanup.Token); }
            catch (Exception exception) when (exception is not OperationCanceledException || cleanup.IsCancellationRequested)
            {
                logger.LogWarning("ETL claim cleanup remains recoverable after {FailureType}.", exception.GetType().Name);
            }
        }
    }

    private EtlClaimHeartbeat Heartbeat(Func<IEtlRuntimeStore, CancellationToken, ValueTask<EtlStoreStatus>> renew, CancellationToken token) =>
        new(scopeFactory, renew, clock, logger, token);

    private async ValueTask<EtlAdmissionResult> RefuseAsync(EtlInvocation invocation, string code, CancellationToken token)
    {
        var refusal = await store.RejectAdmissionAsync(invocation, new(code), token);
        var existing = await store.FindRunAsync(invocation.Scope, invocation.RunId, token);
        return existing is null ? new(refusal.Status, null) : Existing(invocation, existing);
    }

    private static EtlAdmissionResult Existing(EtlInvocation invocation, EtlRunView existing) =>
        EtlRuntimeIdentity.InvocationFingerprint(invocation) == EtlRuntimeIdentity.InvocationFingerprint(existing.Invocation)
            ? new(EtlStoreStatus.Replay, existing)
            : new(EtlStoreStatus.Conflict, null);

    private ValueTask<EtlTerminalSettlementResult> SettleAsync(EtlPreparationLease lease, EtlPreparationDiagnostic diagnostic, CancellationToken token) =>
        store.SettleTerminalAsync(new(lease.Scope, lease.RunId, Outcome(diagnostic), SafeDiagnostic(diagnostic),
            PreparationOwner: lease.Owner, PreparationFence: lease.Fence), token);

    private ValueTask<EtlTerminalSettlementResult> SettleAsync(EtlPartitionLease lease, EtlPreparationDiagnostic diagnostic, CancellationToken token) =>
        store.SettleTerminalAsync(new(lease.Scope, lease.RunId, Outcome(diagnostic), SafeDiagnostic(diagnostic),
            PartitionOrdinal: lease.PartitionOrdinal, PartitionOwner: lease.Owner, PartitionFence: lease.Fence), token);

    private static EtlTerminalOutcome Outcome(EtlPreparationDiagnostic diagnostic) => diagnostic.Kind == EtlPreparationDiagnosticKind.Fault
        ? EtlTerminalOutcome.Faulted : EtlTerminalOutcome.BusinessError;
    private static EtlRunDiagnostic SafeDiagnostic(EtlPreparationDiagnostic diagnostic) => new(diagnostic.Code, diagnostic.RecordOrdinal, diagnostic.Field is { Length: <= 256 } ? diagnostic.Field : null);
    private static EtlChunk Chunk(EtlObjectReference reference) => new(reference.Key, reference.ContentHash, reference.ByteLength);
    private static EtlAttemptCharge Charge(EtlLimits limits) => new(limits.MaxBatchRecords, limits.MaxBatchBytes,
        Math.Min(limits.MaxLookupBytes, limits.MaxSnapshotBytes), limits.MaxOutputBatchBytes);
}
