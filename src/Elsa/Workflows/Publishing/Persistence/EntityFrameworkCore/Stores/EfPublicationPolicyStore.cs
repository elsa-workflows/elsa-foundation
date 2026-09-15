using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for scoped publication policy state and revision CAS.</summary>
public sealed class EfPublicationPolicyStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IPublicationPolicyStore
{
    public async ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var key = EfPublishingStoreSupport.PolicyKey(workflowDefinitionId);
        var row = await FindEntityAsync(tenantId, key, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
        PublicationPolicy policy,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(policy);

        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var key = EfPublishingStoreSupport.PolicyKey(policy.WorkflowDefinitionId);
        var row = await FindEntityForUpdateAsync(tenantId, key, cancellationToken);

        if (row is null)
        {
            if (expectedRevision != 0)
                return new PublicationPolicyWriteResult(false, policy);

            var saved = policy with { Revision = 1 };
            var created = ToEntity(saved, tenantId, key);
            context.Policies.Add(created);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return new PublicationPolicyWriteResult(true, saved);
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                var winner = await FindAsync(policy.WorkflowDefinitionId, cancellationToken);
                if (winner is not null)
                    return new PublicationPolicyWriteResult(false, winner);
                throw;
            }
            finally
            {
                EfPublishingStoreSupport.Detach(context, created);
            }
        }

        var current = ToModel(row);
        if (current.Revision != expectedRevision)
            return new PublicationPolicyWriteResult(false, current);

        var next = policy with { Revision = checked(current.Revision + 1) };
        CopyMutable(row, next);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new PublicationPolicyWriteResult(true, next);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new PublicationPolicyWriteResult(false, await FindWinnerAsync(policy.WorkflowDefinitionId, next, cancellationToken));
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, row);
        }
    }

    private async Task<PublicationPolicyEntity?> FindEntityAsync(
        string? tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        // Hashes are the only indexed lookup keys. The encoded residuals are checked after a
        // bounded candidate read so a hash collision can never silently alias another policy.
        var candidates = await context.Policies.AsNoTracking()
            .Where(candidate => candidate.TenantIdHash == EfPublishingStoreSupport.TenantHash(tenantId) &&
                                candidate.PolicyKeyHash == EfPublishingStoreSupport.Hash(key))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return null;

        var encodedTenantId = EfPublishingStoreSupport.EncodeNullable(tenantId);
        var encodedKey = EfPublishingStoreSupport.Encode(key);
        if (candidates.Length != 1 ||
            !StringComparer.Ordinal.Equals(candidates[0].TenantId, encodedTenantId) ||
            !StringComparer.Ordinal.Equals(candidates[0].PolicyKey, encodedKey))
            throw new InvalidOperationException("A publication policy hash candidate did not match its encoded identity residual.");

        return candidates[0];
    }

    private async Task<PublicationPolicyEntity?> FindEntityForUpdateAsync(
        string? tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        // A tracking query can return an older instance already held by this DbContext. Read a fresh
        // snapshot first, then attach it with its database revision as the concurrency original value.
        var row = await FindEntityAsync(tenantId, key, cancellationToken);
        if (row is null)
            return null;

        DetachTracked(row.Id);
        context.Policies.Attach(row);
        return row;
    }

    private void DetachTracked(string id)
    {
        var tracked = context.ChangeTracker.Entries<PublicationPolicyEntity>()
            .FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.Entity.Id, id));
        tracked?.State = EntityState.Detached;
    }

    private async ValueTask<PublicationPolicy> FindWinnerAsync(
        string? workflowDefinitionId,
        PublicationPolicy fallback,
        CancellationToken cancellationToken)
    {
        var winner = await FindAsync(workflowDefinitionId, cancellationToken);
        return winner ?? fallback;
    }

    private static PublicationPolicyEntity ToEntity(PublicationPolicy policy, string? tenantId, string key)
    {
        var updated = EfPublishingStoreSupport.DateTimeOffsetParts(policy.UpdatedAt);
        return new PublicationPolicyEntity
        {
            Id = EfPublishingStoreSupport.PhysicalId(tenantId, key),
            PolicyKey = EfPublishingStoreSupport.Encode(key),
            PolicyKeyHash = EfPublishingStoreSupport.Hash(key),
            WorkflowDefinitionId = EfPublishingStoreSupport.EncodeNullable(policy.WorkflowDefinitionId),
            WorkflowDefinitionIdHash = policy.WorkflowDefinitionId is null ? null : EfPublishingStoreSupport.Hash(policy.WorkflowDefinitionId),
            TenantId = EfPublishingStoreSupport.EncodeNullable(tenantId),
            TenantIdHash = EfPublishingStoreSupport.TenantHash(tenantId),
            DefaultAction = policy.DefaultAction.ToString(),
            DefaultSlotName = EfPublishingStoreSupport.Encode(policy.DefaultSlotName),
            Revision = policy.Revision,
            UpdatedAtUtcTicks = updated.UtcTicks,
            UpdatedAtOffsetMinutes = updated.OffsetMinutes
        };
    }

    private static void CopyMutable(PublicationPolicyEntity row, PublicationPolicy policy)
    {
        Validate(policy);
        var updated = EfPublishingStoreSupport.DateTimeOffsetParts(policy.UpdatedAt);
        row.DefaultAction = policy.DefaultAction.ToString();
        row.DefaultSlotName = EfPublishingStoreSupport.Encode(policy.DefaultSlotName);
        row.Revision = policy.Revision;
        row.UpdatedAtUtcTicks = updated.UtcTicks;
        row.UpdatedAtOffsetMinutes = updated.OffsetMinutes;
    }

    private static PublicationPolicy ToModel(PublicationPolicyEntity row)
    {
        var policyKey = EfPublishingStoreSupport.DecodeValue(row.PolicyKey, PublishingPolicyProjectionEfModule.PolicyKeyMaximumLength, nameof(row.PolicyKey));
        var tenantId = EfPublishingStoreSupport.DecodeNullableIdentity(row.TenantId, nameof(row.TenantId));
        if (!StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(tenantId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(tenantId, policyKey)))
            throw new InvalidOperationException("The persisted publication policy scope projection is corrupt.");
        EfPublishingStoreSupport.EnsureHash(policyKey, row.PolicyKeyHash, nameof(row.PolicyKey));
        var workflowDefinitionId = EfPublishingStoreSupport.DecodeNullableIdentity(row.WorkflowDefinitionId, nameof(row.WorkflowDefinitionId));
        if (workflowDefinitionId is null)
        {
            if (!StringComparer.Ordinal.Equals(policyKey, "host") || row.WorkflowDefinitionIdHash is not null)
                throw new InvalidOperationException("The persisted publication policy identity projection is corrupt.");
        }
        else
        {
            EfPublishingStoreSupport.EnsureHash(workflowDefinitionId, row.WorkflowDefinitionIdHash ?? "", nameof(row.WorkflowDefinitionId));
            if (!StringComparer.Ordinal.Equals(policyKey, EfPublishingStoreSupport.PolicyKey(workflowDefinitionId)))
                throw new InvalidOperationException("The persisted publication policy key projection is corrupt.");
        }

        if (!Enum.TryParse<PublicationPolicyDefaultAction>(row.DefaultAction, ignoreCase: false, out var action) ||
            !Enum.IsDefined(action) || !StringComparer.Ordinal.Equals(row.DefaultAction, action.ToString()))
            throw new InvalidOperationException("Malformed persisted publication policy: default action is invalid.");
        var defaultSlotName = EfPublishingStoreSupport.DecodeIdentity(row.DefaultSlotName, nameof(row.DefaultSlotName));
        var updatedAt = EfPublishingStoreSupport.DateTimeOffset(row.UpdatedAtUtcTicks, row.UpdatedAtOffsetMinutes);
        if (row.Revision < 1)
            throw new InvalidOperationException("Malformed persisted publication policy: revision is invalid.");
        return new PublicationPolicy(workflowDefinitionId, action, defaultSlotName, row.Revision, updatedAt);
    }

    private static void Validate(PublicationPolicy policy)
    {
        if (policy.WorkflowDefinitionId is not null)
            EfPublishingStoreSupport.EnsureIdentity(policy.WorkflowDefinitionId, nameof(policy.WorkflowDefinitionId));
        EfPublishingStoreSupport.EnsureIdentity(policy.DefaultSlotName, nameof(policy.DefaultSlotName));
        if (!Enum.IsDefined(policy.DefaultAction))
            throw new ArgumentException("Publication policy default action is invalid.", nameof(policy));
    }
}
