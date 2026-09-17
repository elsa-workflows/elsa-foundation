using System.Globalization;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF adapter for create-only activity-publication receipts. A receipt is keyed by its operation tenant and
/// idempotency key inside the caller's persistence scope, and an existing receipt is never replaced.
/// </summary>
public sealed class EfActivityPublicationReceiptStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IActivityPublicationReceiptStore
{
    public async ValueTask<ActivityPublicationReceipt?> FindAsync(
        string? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(idempotencyKey, nameof(idempotencyKey));
        cancellationToken.ThrowIfCancellationRequested();
        // Authorization precedes disclosure: another tenant's receipt is refused before any row is read.
        accessContextAccessor.Current.EnsureTenantScope(tenantId);
        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityAsync(scope, tenantId, idempotencyKey, cancellationToken);
        return row is null ? null : ToModel(row, scope, tenantId, idempotencyKey);
    }

    public async ValueTask<bool> TryCreateAsync(ActivityPublicationReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(receipt);
        accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        if (await FindEntityAsync(scope, receipt.TenantId, receipt.IdempotencyKey, cancellationToken) is not null)
            return false;

        var created = ToEntity(receipt, scope);
        context.ActivityPublicationReceipts.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            // The unique receipt key is the create-only authority. Report a lost race only when the receipt
            // is now actually present; any other failure is not a benign duplicate.
            if (await FindEntityAsync(scope, receipt.TenantId, receipt.IdempotencyKey, cancellationToken) is not null)
                return false;
            throw;
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, created);
        }
    }

    private async Task<ActivityPublicationReceiptEntity?> FindEntityAsync(
        string? scope,
        string? receiptTenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var receiptKey = ReceiptKey(receiptTenantId, idempotencyKey);
        var candidates = await context.ActivityPublicationReceipts.AsNoTracking()
            .Where(row => row.TenantIdHash == EfPublishingStoreSupport.TenantHash(scope) &&
                          row.ReceiptKeyHash == EfPublishingStoreSupport.Hash(receiptKey))
            .OrderBy(row => row.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return null;

        if (candidates.Length != 1 ||
            !StringComparer.Ordinal.Equals(candidates[0].TenantId, EfPublishingStoreSupport.EncodeNullable(scope)) ||
            !StringComparer.Ordinal.Equals(candidates[0].ReceiptTenantId, EfPublishingStoreSupport.EncodeNullable(receiptTenantId)) ||
            !StringComparer.Ordinal.Equals(candidates[0].IdempotencyKey, EfPublishingStoreSupport.Encode(idempotencyKey)))
            throw new InvalidOperationException("An activity-publication receipt hash candidate did not match its encoded identity residual.");
        return candidates[0];
    }

    /// <summary>
    /// The receipt's logical key inside one persistence scope. The tenant is length-framed ahead of the
    /// idempotency key, so no tenant and key pair can be spelled to collide with another, or with a receipt
    /// whose operation carried no tenant.
    /// </summary>
    internal static string ReceiptKey(string? tenantId, string idempotencyKey) =>
        tenantId is null
            ? $"global\n{idempotencyKey}"
            : $"tenant\n{tenantId.Length.ToString(CultureInfo.InvariantCulture)}\n{tenantId}\n{idempotencyKey}";

    private static ActivityPublicationReceiptEntity ToEntity(ActivityPublicationReceipt receipt, string? scope)
    {
        var receiptKey = ReceiptKey(receipt.TenantId, receipt.IdempotencyKey);
        return new ActivityPublicationReceiptEntity
        {
            Id = EfPublishingStoreSupport.PhysicalId(scope, receiptKey),
            ReceiptKeyHash = EfPublishingStoreSupport.Hash(receiptKey),
            IdempotencyKey = EfPublishingStoreSupport.Encode(receipt.IdempotencyKey),
            ReceiptTenantId = EfPublishingStoreSupport.EncodeNullable(receipt.TenantId),
            Status = receipt.Status.ToString(),
            SchemaVersion = PublishingLedgerEfModule.ContentSchemaVersion,
            Content = PublishingEfJson.Serialize(receipt),
            TenantId = EfPublishingStoreSupport.EncodeNullable(scope),
            TenantIdHash = EfPublishingStoreSupport.TenantHash(scope)
        };
    }

    private static ActivityPublicationReceipt ToModel(
        ActivityPublicationReceiptEntity row,
        string? scope,
        string? receiptTenantId,
        string idempotencyKey)
    {
        var receiptKey = ReceiptKey(receiptTenantId, idempotencyKey);
        if (!StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(scope, receiptKey)) ||
            !StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(scope)) ||
            !StringComparer.Ordinal.Equals(row.ReceiptKeyHash, EfPublishingStoreSupport.Hash(receiptKey)))
            throw new InvalidOperationException("The persisted activity-publication receipt identity projection is corrupt.");
        if (!StringComparer.Ordinal.Equals(row.SchemaVersion, PublishingLedgerEfModule.ContentSchemaVersion))
            throw new InvalidOperationException($"Malformed persisted publication state: activity-publication receipt schema version '{row.SchemaVersion}' is not supported.");

        var receipt = PublishingEfJson.Deserialize<ActivityPublicationReceipt>(row.Content, "activity-publication receipt");
        if (!StringComparer.Ordinal.Equals(receipt.TenantId, receiptTenantId) ||
            !StringComparer.Ordinal.Equals(receipt.IdempotencyKey, idempotencyKey))
            throw new InvalidOperationException("The activity publication receipt tenant or key does not match its storage identity.");
        if (!Enum.IsDefined(receipt.Status) || !StringComparer.Ordinal.Equals(row.Status, receipt.Status.ToString()))
            throw new InvalidOperationException("Malformed persisted activity-publication receipt: the status projection does not match its material.");
        if (receipt.Diagnostics is null)
            throw new InvalidOperationException("Malformed persisted activity-publication receipt: diagnostics are missing.");
        return receipt;
    }

    private static void Validate(ActivityPublicationReceipt receipt)
    {
        EfPublishingStoreSupport.EnsureIdentity(receipt.IdempotencyKey, nameof(receipt.IdempotencyKey));
        if (receipt.TenantId is not null)
            EfPublishingStoreSupport.EnsureIdentity(receipt.TenantId, nameof(receipt.TenantId));
        if (!Enum.IsDefined(receipt.Status))
            throw new ArgumentException("The activity-publication receipt status is invalid.", nameof(receipt));
        ArgumentNullException.ThrowIfNull(receipt.Diagnostics, nameof(receipt));
    }
}
