using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF adapter for immutable collection uploads (L01) and completed apply receipts (L02). Rows are
/// partitioned by the exact tenant-plus-user operation scope, found by hashed identity, and proven against
/// their encoded residuals and canonical JSON before a domain value is returned.
/// </summary>
public sealed class EfReusableActivityImportOperationStore(
    Elsa3ImportDbContext db,
    IPersistenceAccessContextAccessor accessContextAccessor) : IReusableActivityImportOperationStore
{
    public async ValueTask<bool> TryCreateCollectionAsync(
        ReusableActivityImportCollectionHandle collection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        Elsa3ImportScopeGuard.EnsureCurrent(accessContextAccessor, collection.AccessScope, hideMismatch: false, "current");
        Elsa3ImportCollectionRecord record;
        try
        {
            record = Elsa3ImportRecordCodec.ToRecord(collection);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReusableActivityImportPersistenceException("create collection", collection.Handle, exception);
        }

        db.Collections.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            // The handle is already taken in this scope; the caller allocates a fresh one.
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
        finally
        {
            // A failed save leaves the row Added; never let a later save in this scope commit it.
            db.Entry(record).State = EntityState.Detached;
        }
    }

    public async ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(accessScope);
        Elsa3ImportScopeGuard.EnsureCurrent(accessContextAccessor, accessScope, hideMismatch: true, "current");
        try
        {
            var tenantKey = Elsa3ImportRecordCodec.TenantKey(accessScope.TenantId);
            var userIdHash = Elsa3ImportRecordCodec.Hash(accessScope.UserId);
            var handleHash = Elsa3ImportRecordCodec.Hash(handle);
            var candidates = await db.Collections.AsNoTracking()
                .Where(row => row.TenantKey == tenantKey && row.UserIdHash == userIdHash && row.HandleHash == handleHash)
                .OrderBy(row => row.TenantKey).ThenBy(row => row.UserIdHash).ThenBy(row => row.HandleHash)
                .Take(2)
                .ToListAsync(cancellationToken);
            return candidates.Count switch
            {
                0 => null,
                1 => Elsa3ImportRecordCodec.ReadCollection(candidates[0], handle, accessScope),
                _ => throw new InvalidDataException("The Elsa 3 import collection identity resolves to more than one row.")
            };
        }
        catch (OperationCanceledException)
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
        Elsa3ImportScopeGuard.EnsureCurrent(accessContextAccessor, accessScope, hideMismatch: true, "current");
        var receiptId = ReusableActivityImportIdentity.Receipt(idempotencyKey, accessScope);
        try
        {
            return (await FindReceiptRecordAsync(db, receiptId, accessScope, cancellationToken))?.Receipt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("load receipt", receiptId, exception);
        }
    }

    /// <summary>Reads one receipt row, proven against its canonical content, or null when absent.</summary>
    internal static async Task<StoredReceipt?> FindReceiptRecordAsync(
        Elsa3ImportDbContext db,
        string receiptId,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken)
    {
        var tenantKey = Elsa3ImportRecordCodec.TenantKey(accessScope.TenantId);
        var userIdHash = Elsa3ImportRecordCodec.Hash(accessScope.UserId);
        var receiptIdHash = Elsa3ImportRecordCodec.Hash(receiptId);
        var candidates = await db.Receipts.AsNoTracking()
            .Where(row => row.TenantKey == tenantKey && row.UserIdHash == userIdHash && row.ReceiptIdHash == receiptIdHash)
            .OrderBy(row => row.TenantKey).ThenBy(row => row.UserIdHash).ThenBy(row => row.ReceiptIdHash)
            .Take(2)
            .ToListAsync(cancellationToken);
        return candidates.Count switch
        {
            0 => null,
            1 => new StoredReceipt(Elsa3ImportRecordCodec.ReadReceipt(candidates[0], receiptId, accessScope), candidates[0].CommitAttemptId),
            _ => throw new InvalidDataException("The Elsa 3 import receipt identity resolves to more than one row.")
        };
    }
}

/// <summary>A durable receipt and the commit attempt that wrote it.</summary>
internal sealed record StoredReceipt(ReusableActivityImportReceipt Receipt, string CommitAttemptId);
