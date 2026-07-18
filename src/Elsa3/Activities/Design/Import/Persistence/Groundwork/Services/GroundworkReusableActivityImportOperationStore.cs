using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Groundwork.Documents.Store;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;

public sealed class GroundworkReusableActivityImportOperationStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IReusableActivityImportOperationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryCreateCollectionAsync(
        ReusableActivityImportCollectionHandle collection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        EnsureCurrentScope(collection.AccessScope, hideMismatch: false);
        try
        {
            var result = await store.SaveAsync(
                new(
                    Elsa3ImportStorageManifest.CollectionDocumentKind,
                    collection.Handle,
                    Elsa3ImportStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(new CollectionDocument(collection), Json),
                    0),
                cancellationToken);
            return result.Status == DocumentStoreWriteStatus.Saved;
        }
        catch (DocumentAtomicWriteException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("create collection", collection.Handle, exception);
        }
    }

    public async ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(accessScope);
        EnsureCurrentScope(accessScope, hideMismatch: true);
        try
        {
            var envelope = await store.LoadAsync(Elsa3ImportStorageManifest.CollectionDocumentKind, handle, cancellationToken);
            if (envelope is null)
                return null;
            var collection = JsonSerializer.Deserialize<CollectionDocument>(envelope.ContentJson, Json)?.Collection
                             ?? throw new JsonException("Collection document is empty.");
            return SameScope(collection.AccessScope, accessScope) ? collection : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("load collection", handle, exception);
        }
    }

    public async ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(accessScope);
        EnsureCurrentScope(accessScope, hideMismatch: true);
        var receiptId = Elsa3ImportStorageManifest.ReceiptId(idempotencyKey, accessScope);
        try
        {
            var envelope = await store.LoadAsync(Elsa3ImportStorageManifest.ReceiptDocumentKind, receiptId, cancellationToken);
            if (envelope is null)
                return null;
            var receipt = JsonSerializer.Deserialize<ReceiptDocument>(envelope.ContentJson, Json)?.Receipt
                          ?? throw new JsonException("Receipt document is empty.");
            return SameScope(receipt.AccessScope, accessScope) ? receipt : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("load receipt", receiptId, exception);
        }
    }

    internal static SaveDocumentRequest SaveReceipt(ReusableActivityImportReceipt receipt)
    {
        try
        {
            return new(
                Elsa3ImportStorageManifest.ReceiptDocumentKind,
                receipt.ReceiptId,
                Elsa3ImportStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(new ReceiptDocument(receipt), Json),
                0);
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("serialize receipt", receipt.ReceiptId, exception);
        }
    }

    internal static ReusableActivityImportReceipt ReadReceipt(DocumentEnvelope envelope)
    {
        try
        {
            return JsonSerializer.Deserialize<ReceiptDocument>(envelope.ContentJson, Json)?.Receipt
                   ?? throw new JsonException("Receipt document is empty.");
        }
        catch (ReusableActivityImportPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("deserialize receipt", envelope.Id, exception);
        }
    }

    private void EnsureCurrentScope(
        ReusableActivityImportAccessScope accessScope,
        bool hideMismatch)
    {
        try
        {
            if (accessScope.TenantId is { } tenantId)
            {
                accessContextAccessor.Current.EnsureScope(new PersistenceScope(tenantId));
                return;
            }

            if (!accessContextAccessor.Current.IsGlobal)
                throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
        }
        catch (InvalidOperationException exception)
        {
            if (hideMismatch)
                throw new ReusableActivityImportNotFoundException("The Elsa 3 import resource was not found.");
            throw new ReusableActivityImportPersistenceException(
                "validate persistence scope",
                "current",
                exception);
        }
    }

    private static bool SameScope(ReusableActivityImportAccessScope left, ReusableActivityImportAccessScope right) =>
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        StringComparer.Ordinal.Equals(left.UserId, right.UserId);

    private sealed record CollectionDocument(ReusableActivityImportCollectionHandle Collection);
    private sealed record ReceiptDocument(ReusableActivityImportReceipt Receipt);
}
