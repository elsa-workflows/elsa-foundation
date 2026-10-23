using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests.WorkerHost;

/// <summary>Pauses one child worker at a selected durable connector transition.</summary>
internal sealed class ConnectorWorkerGate(string root, string role, EtlConnectorWorkerGate selectedGate)
{
    private readonly TaskCompletionSource _checkpointPublication =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checkpointStarted;

    public Task BeforeCaptureAsync(EtlConnectorPartitionLease lease, EtlRetainedHttpPage page,
        CancellationToken cancellationToken) =>
        PauseAsync(EtlConnectorWorkerGate.BeforeCapture, lease, null, page, cancellationToken);

    public Task AfterReservationAsync(EtlConnectorPartitionLease lease, string batchId,
        CancellationToken cancellationToken) =>
        PauseAsync(EtlConnectorWorkerGate.AfterReservation, lease, batchId, CurrentPage(lease), cancellationToken);

    public Task AfterAcknowledgementAsync(EtlConnectorWorkerGate gate, EtlConnectorPartitionLease lease,
        string batchId, CancellationToken cancellationToken) =>
        PauseAsync(gate, lease, batchId, CurrentPage(lease), cancellationToken);

    public Task BeforeAcceptanceAsync(EtlConnectorPartitionLease lease, string batchId,
        CancellationToken cancellationToken) =>
        PauseAsync(EtlConnectorWorkerGate.BeforeAcceptance, lease, batchId, CurrentPage(lease), cancellationToken);

    private async Task PauseAsync(EtlConnectorWorkerGate gate, EtlConnectorPartitionLease lease,
        string? batchId, EtlRetainedHttpPage? page, CancellationToken cancellationToken)
    {
        if (gate == EtlConnectorWorkerGate.None || gate != selectedGate)
            return;

        if (Interlocked.CompareExchange(ref _checkpointStarted, 1, 0) == 0)
        {
            try
            {
                await EtlWorkerProtocol.WriteConnectorCheckpointAsync(root, role,
                    new(gate, lease, batchId, page, Environment.ProcessId), cancellationToken);
                _checkpointPublication.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                _checkpointPublication.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                _checkpointPublication.TrySetException(exception);
            }
        }

        await _checkpointPublication.Task.WaitAsync(cancellationToken);
        var releasePath = EtlWorkerProtocol.ReleasePath(root, role);
        while (!File.Exists(releasePath))
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
    }

    private static EtlRetainedHttpPage? CurrentPage(EtlConnectorPartitionLease lease) =>
        lease.Position is EtlConnectorHttpPosition position ? position.Page : null;
}

/// <summary>Preserves the optional connector-store capability while adding deterministic process gates.</summary>
internal sealed class ConnectorWorkerGateStore(IEtlRuntimeStore baseStore, IEtlConnectorRuntimeStore connector,
    ConnectorWorkerGate gate) : ForwardingEtlConnectorRuntimeStore(baseStore, connector)
{
    private readonly ConnectorWorkerGate _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public override async ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(
        EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page,
        DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.BeforeCaptureAsync(lease, page, cancellationToken);
        return await Connector.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc,
            cancellationToken);
    }

    public override async ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(
        EtlConnectorPartitionLease lease, EtlConnectorBatchDescriptor batch,
        CancellationToken cancellationToken = default)
    {
        var result = await Connector.ReserveConnectorBatchAsync(lease, batch, cancellationToken);
        if (result.Status == EtlStoreStatus.Applied && result.Reservation is { } reservation)
            await _gate.AfterReservationAsync(lease, reservation.Batch.BatchId, cancellationToken);
        return result;
    }

    public override async ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(
        EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route,
        EtlHttpDeliveryAttempt attempt, EtlHttpDeliveryResponse response,
        CancellationToken cancellationToken = default)
    {
        var result = await Connector.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, attempt, response,
            cancellationToken);
        var gate = route switch
        {
            EtlOutputRoute.Main => EtlConnectorWorkerGate.AfterMainAcknowledgement,
            EtlOutputRoute.Error => EtlConnectorWorkerGate.AfterErrorAcknowledgement,
            _ => EtlConnectorWorkerGate.None
        };
        if (result.Status == EtlStoreStatus.Applied && result.Delivery?.Phase == EtlHttpDeliveryPhase.Acknowledged)
            await _gate.AfterAcknowledgementAsync(gate, lease, batchId, cancellationToken);
        return result;
    }

    public override async ValueTask<EtlConnectorPositionResult> AcceptConnectorBatchAsync(EtlConnectorPartitionLease lease,
        string batchId, CancellationToken cancellationToken = default)
    {
        await _gate.BeforeAcceptanceAsync(lease, batchId, cancellationToken);
        return await Connector.AcceptConnectorBatchAsync(lease, batchId, cancellationToken);
    }
}
