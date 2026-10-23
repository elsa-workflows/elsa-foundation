using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>Runs one bounded connector attempt within the shared ETL worker's physical capacity.</summary>
/// <remarks>Each attempt yields after a batch or an empty page; durable delayed work never sleeps in a slot.</remarks>
internal sealed class EtlConnectorExecution(
    IEtlRuntimeStore common,
    IEtlConnectorRuntimeStore store,
    HostFileBindings files,
    HostHttpBindings http,
    EtlRuntimeOptions options,
    EtlConnectorExecutableReader reader,
    EtlConnectorPreparer preparer,
    EtlHttpTransport transport,
    IFileChunkPublisherFactory publishers,
    EtlAttemptBudget budget,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<EtlConnectorExecution> logger)
{
    public async ValueTask PrepareAsync(EtlPreparationLease lease, CancellationToken cancellationToken = default)
    {
        var heartbeat = new EtlClaimHeartbeat(scopes,
            (IEtlRuntimeStore runtime, CancellationToken token) => runtime.RenewPreparationAsync(lease, token),
            clock, logger, cancellationToken);
        var token = heartbeat.Token;
        IAsyncDisposable? permit = null;
        try
        {
            var view = await store.FindConnectorRunAsync(lease.Scope, lease.RunId, token);
            if (view is not { Run.Status: EtlRunStatus.Preparing }) return;
            var pipeline = reader.Read(view.Run.Invocation.Artifact);
            permit = await budget.AcquireAsync(Charge(pipeline), token);
            var prepared = await preparer.PrepareAsync(new(view.Run.Invocation, view.Bindings), token);
            if (prepared.Diagnostic is { } diagnostic)
            {
                await SettleAsync(lease, diagnostic, token);
                return;
            }
            var status = await store.CommitConnectorPreparationAsync(lease, prepared.Prepared!, token);
            if (status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal))
                await SettleAsync(lease, new("etl.connector-preparation-refused"), token);
        }
        catch (EtlExecutableException exception)
        {
            await SettleAsync(lease, new(exception.Code), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await using var retainedPermit = permit;
            await heartbeat.DisposeAsync();
        }
    }

    public async ValueTask ExecutePartitionAsync(EtlConnectorPartitionLease initialLease,
        CancellationToken cancellationToken = default)
    {
        var lease = initialLease;
        var heartbeat = new EtlClaimHeartbeat(scopes,
            (IServiceProvider provider, CancellationToken token) => provider.GetRequiredService<IEtlConnectorRuntimeStore>()
                .RenewConnectorPartitionAsync(Volatile.Read(ref lease), token), clock, logger, cancellationToken);
        var token = heartbeat.Token;
        IAsyncDisposable? permit = null;
        try
        {
            var view = await store.FindConnectorRunAsync(lease.Scope, lease.RunId, token);
            if (view is not { Run.Status: EtlRunStatus.Running, Prepared: not null }) return;
            var pipeline = reader.Read(view.Run.Invocation.Artifact);
            permit = await budget.AcquireAsync(Charge(pipeline), token);
            var admission = new EtlConnectorRunAdmission(view.Run.Invocation, view.Bindings);
            // Verify retained file/lookup inputs before initiating further source work or output effects.
            await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(options.SnapshotRoot,
                admission, view.Prepared, lease.PartitionOrdinal, token);
            EtlCachedHttpPage? page = null;
            if (lease.Position is EtlConnectorHttpPosition position)
            {
                if (position.Exhausted) return;
                var acquired = await new EtlHttpSourceAcquirer(store, http, transport, options.SnapshotRoot, clock)
                    .AcquireAsync(admission, lease, token);
                if (acquired.FailureCode is { } failure)
                {
                    await SettleAsync(lease, new(failure), token);
                    return;
                }
                if (acquired.Lease is null || acquired.Page is null) return;
                Volatile.Write(ref lease, acquired.Lease);
                page = acquired.Page;
                if (page.Descriptor.RecordCount == 0)
                {
                    var advanced = await store.AdvanceEmptyHttpPageAsync(lease, token);
                    if (advanced.Lease is { } updated) Volatile.Write(ref lease, updated);
                    else if (advanced.Status is not (EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or
                                 EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict))
                        await SettleAsync(lease, new("etl.http.empty-page-transition-refused"), token);
                    return;
                }
            }
            var built = lease.OwedBatch is { } owed
                ? await builder.RebuildAsync(owed.Batch, lease.Position, page, token)
                : await builder.BuildNextAsync(lease.Position, page, token);
            if (built.Diagnostic is not null || built.Batch is null)
            {
                await SettleAsync(lease, built.Diagnostic ?? new("etl.connector-batch-cursor-mismatch"), token);
                return;
            }
            var input = built.Batch;
            var reserved = await store.ReserveConnectorBatchAsync(lease, input.Descriptor, token);
            if (reserved.Status is EtlStoreStatus.BudgetExceeded or EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal) return;
            if (reserved.Status == EtlStoreStatus.PermanentBudgetExceeded)
            {
                await SettleAsync(lease, new("etl.connector-batch-retention-limit"), token);
                return;
            }
            if (reserved.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay) ||
                reserved.Reservation is not { Settled: false } reservation || reservation.Batch != input.Descriptor)
            {
                await SettleAsync(lease, new("etl.connector-batch-reservation-conflict"), token);
                return;
            }
            Volatile.Write(ref lease, lease with { OwedBatch = reservation });
            if (await store.RenewConnectorPartitionAsync(lease, token) is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay)) return;
            var output = new EtlConnectorOutputProcessor(store, files, http, publishers, transport, options.SnapshotRoot);
            var retained = await output.TransformAndRetainAsync(lease, admission, pipeline, input, token);
            if (retained.FailureCode is { } outputFailure)
            {
                await SettleAsync(lease, new(outputFailure), token);
                return;
            }
            if (retained.Output is null) return;
            Volatile.Write(ref lease, lease with { OwedBatch = reservation with { Output = retained.Output } });
            var dispatched = await output.DispatchAsync(lease, admission, pipeline, retained.Output, token);
            if (dispatched.FailureCode is { } dispatchFailure)
            {
                await SettleAsync(lease, new(dispatchFailure), token);
                return;
            }
            if (!dispatched.ReadyToAccept) return;
            var accepted = await store.AcceptConnectorBatchAsync(lease, input.Descriptor.BatchId, token);
            if (accepted.Status == EtlStoreStatus.BudgetExceeded) return;
            if (accepted.Status == EtlStoreStatus.PermanentBudgetExceeded)
            {
                await SettleAsync(lease, new("etl.connector-acceptance-retention-limit"), token);
                return;
            }
            if (accepted.Lease is { } next) Volatile.Write(ref lease, next);
            else if (accepted.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal))
                await SettleAsync(lease, new("etl.connector-batch-acceptance-conflict"), token);
        }
        catch (EtlExecutableException exception)
        {
            await SettleAsync(lease, new(exception.Code), token);
        }
        catch (EtlConnectorInputBatchBuilderException exception)
        {
            await SettleAsync(lease, exception.Diagnostic, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            // A replacement's lease never releases this live attempt's process-local allowances.
            await using var retainedPermit = permit;
            await heartbeat.DisposeAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
            try { await store.ReleaseConnectorPartitionAsync(lease, cleanup.Token); }
            catch (Exception exception) when (exception is not OperationCanceledException || cleanup.IsCancellationRequested)
            {
                logger.LogWarning("ETL connector claim cleanup remains recoverable after {FailureType}.", exception.GetType().Name);
            }
        }
    }

    private ValueTask<EtlTerminalSettlementResult> SettleAsync(EtlPreparationLease lease,
        EtlPreparationDiagnostic diagnostic, CancellationToken token) => common.SettleTerminalAsync(
        new(lease.Scope, lease.RunId, EtlRuntimeMetadata.Outcome(diagnostic), EtlRuntimeMetadata.SafeDiagnostic(diagnostic),
            PreparationOwner: lease.Owner, PreparationFence: lease.Fence), token);

    private ValueTask<EtlTerminalSettlementResult> SettleAsync(EtlConnectorPartitionLease lease,
        EtlPreparationDiagnostic diagnostic, CancellationToken token) => common.SettleTerminalAsync(
        new(lease.Scope, lease.RunId, EtlRuntimeMetadata.Outcome(diagnostic), EtlRuntimeMetadata.SafeDiagnostic(diagnostic),
            PartitionOrdinal: lease.PartitionOrdinal, PartitionOwner: lease.Owner, PartitionFence: lease.Fence), token);

    private static EtlAttemptCharge Charge(EtlPipelineV2 pipeline) => new(
        pipeline.Source is EtlHttpJsonSource ? Math.Max(pipeline.Limits.MaxBatchRecords, pipeline.Http.MaxPageRecords) : pipeline.Limits.MaxBatchRecords,
        pipeline.Source is EtlHttpJsonSource ? Math.Max(pipeline.Limits.MaxBatchBytes, pipeline.Http.MaxPageBytes) : pipeline.Limits.MaxBatchBytes,
        pipeline.Lookup is null ? 0 : pipeline.Limits.MaxLookupBytes,
        pipeline.Limits.MaxOutputBatchBytes);
}
