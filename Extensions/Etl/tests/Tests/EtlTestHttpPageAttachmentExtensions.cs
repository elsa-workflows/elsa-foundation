using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

internal static class EtlTestHttpPageAttachmentExtensions
{
    internal static async ValueTask<EtlConnectorPositionResult> AttachTestHttpPageAsync(
        this IEtlConnectorRuntimeStore store, EtlConnectorPartitionLease lease, EtlRetainedHttpPage page,
        CancellationToken cancellationToken = default)
    {
        var attached = await store.AttachHttpPageAsync(lease, page, cancellationToken);
        if (attached.Status != EtlStoreStatus.Conflict)
            return attached;

        var begin = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"), cancellationToken);
        if (begin.Status != EtlStoreStatus.Applied || begin.Permit is not { } permit)
            return new EtlConnectorPositionResult(begin.Status, null);

        var captured = await store.CaptureConnectorHttpSourceAsync(lease, permit, page, permit.StartedAt,
            cancellationToken);
        if (captured.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
            return new EtlConnectorPositionResult(captured.Status, null);

        return await store.AttachHttpPageAsync(lease, page, cancellationToken);
    }
}
