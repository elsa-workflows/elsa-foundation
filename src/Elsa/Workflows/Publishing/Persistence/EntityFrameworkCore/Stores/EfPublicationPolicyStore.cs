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
        var row = await QueryScope(tenantId)
            .Where(candidate => candidate.PolicyKeyHash == EfPublishingStoreSupport.Hash(key) && candidate.PolicyKey == key)
            .SingleOrDefaultAsync(cancellationToken);
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
        var keyHash = EfPublishingStoreSupport.Hash(key);
        var row = await FindEntityForUpdateAsync(tenantId, keyHash, key, cancellationToken);

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
    }

    private IQueryable<PublicationPolicyEntity> QueryScope(string? tenantId) =>
        context.Policies.AsNoTracking().Where(row => row.TenantIdHash == EfPublishingStoreSupport.TenantHash(tenantId) && row.TenantId == tenantId);

    private async Task<PublicationPolicyEntity?> FindEntityForUpdateAsync(
        string? tenantId,
        string keyHash,
        string key,
        CancellationToken cancellationToken)
    {
        // A tracking query can return an older instance already held by this DbContext. Read a fresh
        // snapshot first, then attach it with its database revision as the concurrency original value.
        var row = await context.Policies.AsNoTracking()
            .Where(candidate => candidate.TenantIdHash == EfPublishingStoreSupport.TenantHash(tenantId) && candidate.TenantId == tenantId)
            .Where(candidate => candidate.PolicyKeyHash == keyHash && candidate.PolicyKey == key)
            .SingleOrDefaultAsync(cancellationToken);
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
            PolicyKey = key,
            PolicyKeyHash = EfPublishingStoreSupport.Hash(key),
            WorkflowDefinitionId = policy.WorkflowDefinitionId,
            WorkflowDefinitionIdHash = policy.WorkflowDefinitionId is null ? null : EfPublishingStoreSupport.Hash(policy.WorkflowDefinitionId),
            TenantId = tenantId,
            TenantIdHash = EfPublishingStoreSupport.TenantHash(tenantId),
            DefaultAction = policy.DefaultAction.ToString(),
            DefaultSlotName = policy.DefaultSlotName,
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
        row.DefaultSlotName = policy.DefaultSlotName;
        row.Revision = policy.Revision;
        row.UpdatedAtUtcTicks = updated.UtcTicks;
        row.UpdatedAtOffsetMinutes = updated.OffsetMinutes;
    }

    private static PublicationPolicy ToModel(PublicationPolicyEntity row)
    {
        EfPublishingStoreSupport.EnsurePersistedValue(row.PolicyKey, PublishingPolicyProjectionEfModule.PolicyKeyMaximumLength, nameof(row.PolicyKey));
        if (row.TenantId is not null)
            EfPublishingStoreSupport.EnsurePersistedValue(row.TenantId, EfPublishingStoreSupport.IdentityMaximumLength, nameof(row.TenantId));
        if (!StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(row.TenantId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(row.TenantId, row.PolicyKey)))
            throw new InvalidOperationException("The persisted publication policy scope projection is corrupt.");
        EfPublishingStoreSupport.EnsureHash(row.PolicyKey, row.PolicyKeyHash, nameof(row.PolicyKey));
        if (row.WorkflowDefinitionId is null)
        {
            if (!StringComparer.Ordinal.Equals(row.PolicyKey, "host") || row.WorkflowDefinitionIdHash is not null)
                throw new InvalidOperationException("The persisted publication policy identity projection is corrupt.");
        }
        else
        {
            EfPublishingStoreSupport.EnsurePersistedValue(row.WorkflowDefinitionId, EfPublishingStoreSupport.IdentityMaximumLength, nameof(row.WorkflowDefinitionId));
            EfPublishingStoreSupport.EnsureHash(row.WorkflowDefinitionId, row.WorkflowDefinitionIdHash ?? "", nameof(row.WorkflowDefinitionId));
            if (!StringComparer.Ordinal.Equals(row.PolicyKey, EfPublishingStoreSupport.PolicyKey(row.WorkflowDefinitionId)))
                throw new InvalidOperationException("The persisted publication policy key projection is corrupt.");
        }

        if (!Enum.TryParse<PublicationPolicyDefaultAction>(row.DefaultAction, ignoreCase: false, out var action) ||
            !Enum.IsDefined(action) || !StringComparer.Ordinal.Equals(row.DefaultAction, action.ToString()))
            throw new InvalidOperationException("Malformed persisted publication policy: default action is invalid.");
        EfPublishingStoreSupport.EnsurePersistedValue(row.DefaultSlotName, EfPublishingStoreSupport.IdentityMaximumLength, nameof(row.DefaultSlotName));
        var updatedAt = EfPublishingStoreSupport.DateTimeOffset(row.UpdatedAtUtcTicks, row.UpdatedAtOffsetMinutes);
        if (row.Revision < 1)
            throw new InvalidOperationException("Malformed persisted publication policy: revision is invalid.");
        return new PublicationPolicy(row.WorkflowDefinitionId, action, row.DefaultSlotName, row.Revision, updatedAt);
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
