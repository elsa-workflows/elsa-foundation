using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// The one place a publishing receipt is written, and the intent that guarantees it eventually is.
/// <para>
/// On a split host the receipt cannot join the design commit that authorizes it, so it is written afterwards.
/// The publication is already done at that point, which is what makes an unwritten receipt a durability
/// problem rather than a rollback: the caller can crash, and then nothing else is coming. An intent recorded
/// inside the design commit gives the write a second driver — <see cref="GroundworkDesignPostCommitRedrive"/> —
/// so convergence no longer depends on the caller returning.
/// </para>
/// </summary>
public static class ActivityPublicationReceiptDelivery
{
    /// <summary>Selects <see cref="ActivityPublicationReceiptIntentDeliverer"/> when a redrive claims the intent.</summary>
    public const string IntentKind = "elsa.publishing.activity-publication-receipt";

    private static readonly string[] ReceiptKinds =
    [
        PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind
    ];

    /// <summary>
    /// The intent identity for a receipt document. Derived from the receipt's own id — which already folds in
    /// tenant and idempotency key — so a replayed publication records the same intent rather than a second one.
    /// </summary>
    public static string IntentId(string receiptDocumentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptDocumentId);
        return $"{IntentKind}:{receiptDocumentId}";
    }

    /// <summary>The intent that keeps <paramref name="receipt"/> owed until it lands.</summary>
    public static DesignPostCommitIntent CreateIntent(SaveDocumentRequest receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new(
            IntentId(receipt.Id),
            IntentKind,
            new(receipt.DocumentKind, receipt.Id, receipt.SchemaVersion, receipt.ContentJson));
    }

    /// <summary>
    /// Writes the receipt. An already-present, byte-identical receipt is success rather than a conflict — that
    /// is what makes the write safe to repeat, and every driver of it repeats: the caller retries, and the
    /// redrive redelivers whenever a crash lands between a successful write and the intent's deletion.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The id is occupied by a different receipt, so two publications are using one idempotency key.
    /// </exception>
    public static async Task WriteAsync(
        IDocumentStore publishingStore,
        SaveDocumentRequest receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishingStore);
        ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            await publishingStore.SaveAllAsync(DocumentCommitScope.Of(ReceiptKinds), [receipt], cancellationToken);
        }
        catch (DocumentAtomicWriteException exception)
            when (exception.Status is DocumentStoreWriteStatus.ConcurrencyConflict)
        {
            // A create-only write and a racing write surface the same status, so the conflict is only benign
            // when what is already stored is this same receipt. A different publication reusing one
            // idempotency key must not be reported as success.
            await EnsureStoredMatchesAsync(publishingStore, receipt, cancellationToken);
        }
    }

    private static async Task EnsureStoredMatchesAsync(
        IDocumentStore publishingStore,
        SaveDocumentRequest intended,
        CancellationToken cancellationToken)
    {
        var stored = await publishingStore.LoadAsync(intended.DocumentKind, intended.Id, cancellationToken);
        if (stored is null)
        {
            throw new InvalidOperationException(
                $"The activity publication receipt '{intended.Id}' conflicted on write but is not present; " +
                "the publishing target rejected the write for another reason.");
        }

        if (!StringComparer.Ordinal.Equals(stored.ContentJson, intended.ContentJson))
        {
            throw new InvalidOperationException(
                $"Activity publication receipt '{intended.Id}' already exists with different content. Two " +
                "publications are using one idempotency key.");
        }
    }
}

/// <summary>
/// Performs a publishing-receipt intent claimed by the design lane's redrive. It lives here rather than in the
/// design lane because addressing the publishing target is publishing's business; the outbox only knows that
/// some feature answers for this intent kind.
/// </summary>
public sealed class ActivityPublicationReceiptIntentDeliverer(GroundworkLaneStores laneStores)
    : IDesignPostCommitIntentDeliverer
{
    public string IntentKind => ActivityPublicationReceiptDelivery.IntentKind;

    public async ValueTask DeliverAsync(
        DesignPostCommitIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await ActivityPublicationReceiptDelivery.WriteAsync(
            laneStores.For<PublishingGroundworkStorageManifestSource>(),
            intent.Document.ToSaveRequest(),
            cancellationToken);
    }
}
