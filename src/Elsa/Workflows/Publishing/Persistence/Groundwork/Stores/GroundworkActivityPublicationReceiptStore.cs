using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityPublicationReceiptStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(
        store,
        serializer,
        PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind),
        IActivityPublicationReceiptStore
{
    public async ValueTask<ActivityPublicationReceipt?> FindAsync(
        string? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var loaded = await LoadAsync<ActivityPublicationReceipt>(Id(tenantId, idempotencyKey), cancellationToken);
        if (loaded is null)
            return null;
        var receipt = loaded.Value.Document;
        if (!StringComparer.Ordinal.Equals(receipt.TenantId, tenantId))
            throw new InvalidOperationException("The activity publication receipt tenant does not match its storage identity.");
        return receipt;
    }

    public async ValueTask<bool> TryCreateAsync(
        ActivityPublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var result = await SaveAsync(Id(receipt.TenantId, receipt.IdempotencyKey), receipt, 0, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    internal static string Id(string? tenantId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            tenantId is null
                ? $"global\n{idempotencyKey}"
                : $"tenant\n{tenantId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{tenantId}\n{idempotencyKey}")));
}
