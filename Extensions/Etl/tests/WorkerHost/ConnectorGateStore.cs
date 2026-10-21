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
internal sealed class ConnectorWorkerGateStore : ForwardingEtlRuntimeStore, IEtlConnectorRuntimeStore
{
    private readonly IEtlConnectorRuntimeStore _connector;
    private readonly ConnectorWorkerGate _gate;

    public ConnectorWorkerGateStore(IEtlRuntimeStore baseStore, IEtlConnectorRuntimeStore connector,
        ConnectorWorkerGate gate) : base(baseStore)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public ValueTask<EtlConnectorAdmissionResult> AdmitConnectorAsync(EtlConnectorRunAdmission admission,
        CancellationToken cancellationToken = default) =>
        _connector.AdmitConnectorAsync(admission, cancellationToken);

    public ValueTask<EtlConnectorRunView?> FindConnectorRunAsync(EtlScope scope, string runId,
        CancellationToken cancellationToken = default) =>
        _connector.FindConnectorRunAsync(scope, runId, cancellationToken);

    public ValueTask<EtlPreparationLease?> ClaimConnectorPreparationAsync(EtlScope scope, string workerId,
        CancellationToken cancellationToken = default) =>
        _connector.ClaimConnectorPreparationAsync(scope, workerId, cancellationToken);

    public ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease,
        EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default) =>
        _connector.CommitConnectorPreparationAsync(lease, prepared, cancellationToken);

    public ValueTask<EtlConnectorPartitionLease?> ClaimConnectorPartitionAsync(EtlScope scope, string? runId,
        string workerId, CancellationToken cancellationToken = default) =>
        _connector.ClaimConnectorPartitionAsync(scope, runId, workerId, cancellationToken);

    public ValueTask<EtlConnectorHttpSourceFetchResult> BeginConnectorHttpSourceAsync(
        EtlConnectorPartitionLease lease, string requestId, CancellationToken cancellationToken = default) =>
        _connector.BeginConnectorHttpSourceAsync(lease, requestId, cancellationToken);

    public ValueTask<EtlConnectorHttpSourceFetchResult> CompleteConnectorHttpSourceAsync(
        EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome,
        CancellationToken cancellationToken = default) =>
        _connector.CompleteConnectorHttpSourceAsync(lease, attempt, outcome, cancellationToken);

    public async ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(
        EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page,
        DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.BeforeCaptureAsync(lease, page, cancellationToken);
        return await _connector.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc,
            cancellationToken);
    }

    public ValueTask<EtlConnectorPositionResult> AttachHttpPageAsync(EtlConnectorPartitionLease lease,
        EtlRetainedHttpPage page, CancellationToken cancellationToken = default) =>
        _connector.AttachHttpPageAsync(lease, page, cancellationToken);

    public ValueTask<EtlConnectorPositionResult> AdvanceEmptyHttpPageAsync(EtlConnectorPartitionLease lease,
        CancellationToken cancellationToken = default) =>
        _connector.AdvanceEmptyHttpPageAsync(lease, cancellationToken);

    public async ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(
        EtlConnectorPartitionLease lease, EtlConnectorBatchDescriptor batch,
        CancellationToken cancellationToken = default)
    {
        var result = await _connector.ReserveConnectorBatchAsync(lease, batch, cancellationToken);
        if (result.Status == EtlStoreStatus.Applied && result.Reservation is { } reservation)
            await _gate.AfterReservationAsync(lease, reservation.Batch.BatchId, cancellationToken);
        return result;
    }

    public ValueTask<EtlConnectorOutputResult> RetainConnectorBatchOutputAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlConnectorBatchOutput output, CancellationToken cancellationToken = default) =>
        _connector.RetainConnectorBatchOutputAsync(lease, batchId, output, cancellationToken);

    public ValueTask<EtlConnectorHttpDeliveryResult> BeginConnectorHttpDeliveryAsync(
        EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, string requestId,
        CancellationToken cancellationToken = default) =>
        _connector.BeginConnectorHttpDeliveryAsync(lease, batchId, route, requestId, cancellationToken);

    public async ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(
        EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route,
        EtlHttpDeliveryAttempt attempt, EtlHttpDeliveryResponse response,
        CancellationToken cancellationToken = default)
    {
        var result = await _connector.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, attempt, response,
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

    public ValueTask<EtlStoreStatus> RecordConnectorFilePublicationAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlOutputRoute route, EtlObjectReference publication,
        CancellationToken cancellationToken = default) =>
        _connector.RecordConnectorFilePublicationAsync(lease, batchId, route, publication, cancellationToken);

    public async ValueTask<EtlConnectorPositionResult> AcceptConnectorBatchAsync(EtlConnectorPartitionLease lease,
        string batchId, CancellationToken cancellationToken = default)
    {
        await _gate.BeforeAcceptanceAsync(lease, batchId, cancellationToken);
        return await _connector.AcceptConnectorBatchAsync(lease, batchId, cancellationToken);
    }

    public ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease,
        CancellationToken cancellationToken = default) =>
        _connector.RenewConnectorPartitionAsync(lease, cancellationToken);

    public ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease,
        CancellationToken cancellationToken = default) =>
        _connector.ReleaseConnectorPartitionAsync(lease, cancellationToken);
}
