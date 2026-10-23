using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests.Support;

/// <summary>Preserves both runtime capabilities for test decorators that gate individual connector transitions.</summary>
internal abstract class ForwardingEtlConnectorRuntimeStore(IEtlRuntimeStore runtime, IEtlConnectorRuntimeStore connector)
    : ForwardingEtlRuntimeStore(runtime), IEtlConnectorRuntimeStore
{
    protected IEtlConnectorRuntimeStore Connector { get; } = connector ?? throw new ArgumentNullException(nameof(connector));

    public virtual ValueTask<EtlConnectorAdmissionResult> AdmitConnectorAsync(EtlConnectorRunAdmission admission, CancellationToken cancellationToken = default) =>
        Connector.AdmitConnectorAsync(admission, cancellationToken);

    public virtual ValueTask<EtlConnectorRunView?> FindConnectorRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default) =>
        Connector.FindConnectorRunAsync(scope, runId, cancellationToken);

    public virtual ValueTask<EtlPreparationLease?> ClaimConnectorPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default) =>
        Connector.ClaimConnectorPreparationAsync(scope, workerId, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease, EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default) =>
        Connector.CommitConnectorPreparationAsync(lease, prepared, cancellationToken);

    public virtual ValueTask<EtlConnectorPartitionLease?> ClaimConnectorPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default) =>
        Connector.ClaimConnectorPartitionAsync(scope, runId, workerId, cancellationToken);

    public virtual ValueTask<EtlConnectorHttpSourceFetchResult> BeginConnectorHttpSourceAsync(EtlConnectorPartitionLease lease, string requestId, CancellationToken cancellationToken = default) =>
        Connector.BeginConnectorHttpSourceAsync(lease, requestId, cancellationToken);

    public virtual ValueTask<EtlConnectorHttpSourceFetchResult> CompleteConnectorHttpSourceAsync(EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, CancellationToken cancellationToken = default) =>
        Connector.CompleteConnectorHttpSourceAsync(lease, attempt, outcome, cancellationToken);

    public virtual ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default) =>
        Connector.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc, cancellationToken);

    public virtual ValueTask<EtlConnectorPositionResult> AttachHttpPageAsync(EtlConnectorPartitionLease lease, EtlRetainedHttpPage page, CancellationToken cancellationToken = default) =>
        Connector.AttachHttpPageAsync(lease, page, cancellationToken);

    public virtual ValueTask<EtlConnectorPositionResult> AdvanceEmptyHttpPageAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default) =>
        Connector.AdvanceEmptyHttpPageAsync(lease, cancellationToken);

    public virtual ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(EtlConnectorPartitionLease lease, EtlConnectorBatchDescriptor batch, CancellationToken cancellationToken = default) =>
        Connector.ReserveConnectorBatchAsync(lease, batch, cancellationToken);

    public virtual ValueTask<EtlConnectorOutputResult> RetainConnectorBatchOutputAsync(EtlConnectorPartitionLease lease, string batchId, EtlConnectorBatchOutput output, CancellationToken cancellationToken = default) =>
        Connector.RetainConnectorBatchOutputAsync(lease, batchId, output, cancellationToken);

    public virtual ValueTask<EtlConnectorHttpDeliveryResult> BeginConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, string requestId, CancellationToken cancellationToken = default) =>
        Connector.BeginConnectorHttpDeliveryAsync(lease, batchId, route, requestId, cancellationToken);

    public virtual ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, EtlHttpDeliveryAttempt attempt, EtlHttpDeliveryResponse response, CancellationToken cancellationToken = default) =>
        Connector.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, attempt, response, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> RecordConnectorFilePublicationAsync(EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, EtlObjectReference publication, CancellationToken cancellationToken = default) =>
        Connector.RecordConnectorFilePublicationAsync(lease, batchId, route, publication, cancellationToken);

    public virtual ValueTask<EtlConnectorPositionResult> AcceptConnectorBatchAsync(EtlConnectorPartitionLease lease, string batchId, CancellationToken cancellationToken = default) =>
        Connector.AcceptConnectorBatchAsync(lease, batchId, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default) =>
        Connector.RenewConnectorPartitionAsync(lease, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default) =>
        Connector.ReleaseConnectorPartitionAsync(lease, cancellationToken);
}
