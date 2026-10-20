using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests.Support;

internal abstract class ForwardingEtlRuntimeStore(IEtlRuntimeStore inner) : IEtlRuntimeStore
{
    public int MaximumPageSize => inner.MaximumPageSize;

    protected IEtlRuntimeStore Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual ValueTask<EtlAdmissionResult> AdmitAsync(EtlRunAdmission admission, CancellationToken cancellationToken = default) =>
        Inner.AdmitAsync(admission, cancellationToken);

    public virtual ValueTask<EtlTerminalSettlementResult> RejectAdmissionAsync(EtlInvocation invocation, EtlRunDiagnostic diagnostic,
        CancellationToken cancellationToken = default) => Inner.RejectAdmissionAsync(invocation, diagnostic, cancellationToken);

    public virtual ValueTask<EtlRunView?> FindRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default) =>
        Inner.FindRunAsync(scope, runId, cancellationToken);

    public virtual ValueTask<EtlPreparationLease?> ClaimPreparationAsync(EtlScope scope, string workerId,
        CancellationToken cancellationToken = default) => Inner.ClaimPreparationAsync(scope, workerId, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> RenewPreparationAsync(EtlPreparationLease lease,
        CancellationToken cancellationToken = default) => Inner.RenewPreparationAsync(lease, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> CommitPreparationAsync(EtlPreparationLease lease, EtlPreparedRun prepared,
        CancellationToken cancellationToken = default) => Inner.CommitPreparationAsync(lease, prepared, cancellationToken);

    public virtual ValueTask<EtlPartitionLease?> ClaimPartitionAsync(EtlScope scope, string? runId, string workerId,
        CancellationToken cancellationToken = default) => Inner.ClaimPartitionAsync(scope, runId, workerId, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> RenewPartitionAsync(EtlPartitionLease lease,
        CancellationToken cancellationToken = default) => Inner.RenewPartitionAsync(lease, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> ReleasePartitionAsync(EtlPartitionLease lease,
        CancellationToken cancellationToken = default) => Inner.ReleasePartitionAsync(lease, cancellationToken);

    public virtual ValueTask<EtlReservationResult> ReserveBatchAsync(EtlPartitionLease lease, EtlBatchDescriptor batch,
        CancellationToken cancellationToken = default) => Inner.ReserveBatchAsync(lease, batch, cancellationToken);

    public virtual ValueTask<EtlAcceptanceResult> AcceptBatchAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance,
        CancellationToken cancellationToken = default) => Inner.AcceptBatchAsync(lease, acceptance, cancellationToken);

    public virtual ValueTask<EtlTerminalSettlementResult> SettleTerminalAsync(EtlTerminalProposal proposal,
        CancellationToken cancellationToken = default) => Inner.SettleTerminalAsync(proposal, cancellationToken);

    public virtual ValueTask<EtlTerminalSettlementResult> CancelAsync(EtlInvocation invocation,
        CancellationToken cancellationToken = default) => Inner.CancelAsync(invocation, cancellationToken);

    public virtual ValueTask<EtlDeliveryLease?> ClaimDeliveryAsync(EtlScope scope, string workerId,
        CancellationToken cancellationToken = default) => Inner.ClaimDeliveryAsync(scope, workerId, cancellationToken);

    public virtual ValueTask<EtlStoreStatus> AcknowledgeDeliveryAsync(EtlDeliveryLease lease, EtlDeliveryDisposition disposition,
        CancellationToken cancellationToken = default) => Inner.AcknowledgeDeliveryAsync(lease, disposition, cancellationToken);

    public virtual ValueTask<IReadOnlyList<EtlRunView>> ListRunsAsync(EtlScope scope, string? afterRunId, int take,
        CancellationToken cancellationToken = default) => Inner.ListRunsAsync(scope, afterRunId, take, cancellationToken);

    public virtual ValueTask<IReadOnlyList<EtlPartitionView>> ListPartitionsAsync(EtlScope scope, string runId,
        int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default) =>
        Inner.ListPartitionsAsync(scope, runId, afterPartitionOrdinal, take, cancellationToken);

    public virtual ValueTask<IReadOnlyList<EtlManifestEntry>> ListManifestAsync(EtlScope scope, string runId,
        EtlManifestPosition? after, int take, CancellationToken cancellationToken = default) =>
        Inner.ListManifestAsync(scope, runId, after, take, cancellationToken);
}
