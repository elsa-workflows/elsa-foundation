using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityPublicationReceiptStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string? targetName = null)
    : GroundworkPublishingStore(
        sessions,
        accessContextAccessor,
        serializer,
        PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
        targetName),
        IActivityPublicationReceiptStore
{
    public ValueTask<ActivityPublicationReceipt?> FindAsync(
        string? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        AccessContextAccessor.Current.EnsureTenantScope(tenantId);
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
        return ValueTask.FromResult(SaveSucceeded(Id(receipt.TenantId, receipt.IdempotencyKey), receipt, null));
    }

    /// <summary>
    /// The receipt's row, for a publication staging it into a transaction it owns. The receipt is written
    /// in the same transaction as the design and runtime material, so there is no window in which a
    /// publication is durable but unclaimable by its idempotency key.
    /// </summary>
    public static StorageValues Row(ActivityPublicationReceipt receipt, PublishingGroundworkDocumentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(serializer);
        var kind = PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind;
        var (schemaVersion, content) = serializer.Serialize(kind, receipt);
        return new StorageValues(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.IdField] = Id(receipt.TenantId, receipt.IdempotencyKey),
            [PublishingGroundworkStorageManifest.SchemaVersionField] = schemaVersion,
            [PublishingGroundworkStorageManifest.ContentField] = content,
            [PublishingGroundworkStorageManifest.TenantIdField] = receipt.TenantId
        });
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
