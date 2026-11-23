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
    ILogger<EtlConnectorExecution> logger,
    HostSqlBindings? sql = null,
    HostRabbitMqBindings? rabbitMq = null)
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
            // A watermarked SQL source reads only rows above the last completed run's high watermark (spec 1199).
            long? floor = pipeline.Source is EtlSqlSource { Watermark: { } column } sql
                ? await store.FindConnectorSourceWatermarkAsync(lease.Scope,
                    new(view.Run.Invocation.Artifact.DefinitionId, sql.Binding, view.Bindings.Source.Revision.Handle, sql.Table, column), token)
                : null;
            var prepared = await preparer.PrepareAsync(new(view.Run.Invocation, view.Bindings), token, floor);
            // A RabbitMQ source's messages stay unacknowledged until the prepared run is recorded; every other way out
            // disposes the receipt, so the broker requeues them (spec 1201).
            await using var receipt = prepared.Receipt;
            // A transiently unavailable source records nothing; the lapsed lease runs preparation again.
            if (prepared.Transient)
            {
                logger.LogWarning("ETL run {RunId} could not read its source ({Failure}); preparation retries after its lease lapses.",
                    lease.RunId, prepared.TransientCause?.GetType().Name);
                return;
            }
            if (prepared.Diagnostic is { } diagnostic)
            {
                await SettleAsync(lease, diagnostic, token);
                return;
            }
            var status = await store.CommitConnectorPreparationAsync(lease, prepared.Prepared!, token);
            // The recorded run ends this preparation's lease, so its heartbeat may cancel the claim token at any moment now;
            // the acknowledgement uses the caller's token, so only stopping the host can abandon it.
            if (status is EtlStoreStatus.Applied or EtlStoreStatus.Replay && receipt is not null)
                await AcknowledgeAsync(lease, receipt, cancellationToken);
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

    /// <summary>
    /// Acknowledges a recorded snapshot's messages. A failed acknowledgement leaves them unacknowledged; the broker requeues them
    /// and a later run reads them again, the at-least-once window of spec 1201, so it is logged rather than failing this run.
    /// </summary>
    private async ValueTask AcknowledgeAsync(EtlPreparationLease lease, IEtlRabbitMqReceipt receipt, CancellationToken token)
    {
        try
        {
            await receipt.AcknowledgeAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            logger.LogWarning("ETL run {RunId} recorded its RabbitMQ snapshot but could not acknowledge its messages ({Failure}); the broker redelivers them to a later run.",
                lease.RunId, exception.GetType().Name);
        }
    }

    private static bool LeftPage(EtlConnectorPartitionLease before, EtlConnectorPartitionLease after) =>
        before.Position is EtlConnectorHttpPosition left && after.Position is EtlConnectorHttpPosition current &&
        current.PageSequence > left.PageSequence;

    /// <summary>Deletion is best effort: a file left behind costs disk until a later cleanup, never correctness.</summary>
    private async ValueTask DeleteEndedFilesAsync(Func<ValueTask> delete)
    {
        try { await delete(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("ETL connector files whose use ended were left in place after {FailureType}.", exception.GetType().Name);
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
            var acquirer = new EtlHttpSourceAcquirer(store, http, transport, options.SnapshotRoot, clock);
            var continuous = pipeline.Continuous is not null;
            if (lease.Position is EtlConnectorHttpPosition position)
            {
                if (position.Exhausted) return;
                var acquired = await acquirer.AcquireAsync(admission, lease, token);
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
                    if (advanced.Lease is { } updated)
                    {
                        var left = lease;
                        Volatile.Write(ref lease, updated);
                        if (continuous && LeftPage(left, updated))
                            await DeleteEndedFilesAsync(() => acquirer.DeleteLeftPageAsync(admission, left, token));
                    }
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
            var output = new EtlConnectorOutputProcessor(store, files, http, publishers, transport, options.SnapshotRoot, sql, clock, rabbitMq);
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
            if (accepted.Lease is { } next)
            {
                var left = lease;
                Volatile.Write(ref lease, next);
                // A continuous run deletes files once the committed transition ended their use (spec 1196).
                if (continuous)
                {
                    await DeleteEndedFilesAsync(() =>
                    {
                        output.DeleteRetainedRouteBodies(retained.Output);
                        return ValueTask.CompletedTask;
                    });
                    if (LeftPage(left, next))
                        await DeleteEndedFilesAsync(() => acquirer.DeleteLeftPageAsync(admission, left, token));
                }
            }
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
