using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Durable lookup for idempotent activity-publication outcomes. Implementations must create a
/// receipt only when the tenant-owned operation identity is absent and must never replace an
/// existing receipt with a different request fingerprint.
/// </summary>
public interface IActivityPublicationReceiptStore
{
    ValueTask<ActivityPublicationReceipt?> FindAsync(
        string? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryCreateAsync(
        ActivityPublicationReceipt receipt,
        CancellationToken cancellationToken = default);
}
