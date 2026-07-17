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
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var loaded = await LoadAsync<ActivityPublicationReceipt>(Id(idempotencyKey), cancellationToken);
        return loaded?.Document;
    }

    public async ValueTask<bool> TryCreateAsync(
        ActivityPublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var result = await SaveAsync(Id(receipt.IdempotencyKey), receipt, 0, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    private static string Id(string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
}
