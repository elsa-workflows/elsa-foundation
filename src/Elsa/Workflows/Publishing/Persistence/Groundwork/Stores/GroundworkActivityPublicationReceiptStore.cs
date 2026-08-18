using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityPublicationReceiptStore(
    GroundworkPublishingStorage storage,
    PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(storage, serializer, PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind),
        IActivityPublicationReceiptStore
{
    public ValueTask<ActivityPublicationReceipt?> FindAsync(
        string? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<ActivityPublicationReceipt>(Id(tenantId, idempotencyKey));
        if (loaded is null)
            return ValueTask.FromResult<ActivityPublicationReceipt?>(null);

        var receipt = loaded.Value.Document;
        if (!StringComparer.Ordinal.Equals(receipt.TenantId, tenantId))
            throw new InvalidOperationException("The activity publication receipt tenant does not match its storage identity.");
        return ValueTask.FromResult<ActivityPublicationReceipt?>(receipt);
    }

    public ValueTask<bool> TryCreateAsync(
        ActivityPublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Save(Id(receipt.TenantId, receipt.IdempotencyKey), receipt, null));
    }

    /// <summary>
    /// The receipt row for one idempotency key. The tenant is length-prefixed before the key so no tenant
    /// and key pair can be spelled to collide with another, or with a global receipt.
    /// </summary>
    internal static string Id(string? tenantId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            tenantId is null
                ? $"global\n{idempotencyKey}"
                : $"tenant\n{tenantId.Length.ToString(CultureInfo.InvariantCulture)}\n{tenantId}\n{idempotencyKey}")));
}
