using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US4 — Reconciliation-state sibling cardinality + index surface.
/// </summary>
public sealed class ReconciliationStateTests
{
    [Fact]
    public async Task ReconciliationState_IsOneToZeroOrOne_WithParent()
    {
        using var host = ActivitiesDesignTestHost.Create();

        var defId = Guid.NewGuid().ToString("N");

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = defId,
                ActivityTypeKey = "Foo",
                SourceKind = "Json",
                SourceId = "Acme",
                ProvisionedAt = DateTimeOffset.UtcNow,
                ProvisionedBy = "test",
                Category = "Test"
            });

            ctx.ActivityDefinitionReconciliationStates.Add(new ActivityDefinitionReconciliationState
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityDefinitionId = defId,
                LastSeenAt = DateTimeOffset.UtcNow,
                LastProvisionedAt = DateTimeOffset.UtcNow,
                ProvisioningHash = "abc",
            });
            await ctx.SaveChangesAsync();
        }

        // A second sibling for the same parent must violate the unique index.
        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitionReconciliationStates.Add(new ActivityDefinitionReconciliationState
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityDefinitionId = defId,
                LastSeenAt = DateTimeOffset.UtcNow,
                LastProvisionedAt = DateTimeOffset.UtcNow,
                ProvisioningHash = "second-attempt",
            });

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task AdminCreatedDefinition_HasNoReconciliationStateSibling()
    {
        // Admin-created definitions (no source-driven reconciliation) have no sibling.
        // Queries that look for one must return null gracefully.
        using var host = ActivitiesDesignTestHost.Create();

        var defId = Guid.NewGuid().ToString("N");

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = defId,
                ActivityTypeKey = "AdminCreated",
                SourceKind = "Json",
                SourceId = "Acme",
                ProvisionedAt = DateTimeOffset.UtcNow,
                ProvisionedBy = "admin",
                Category = "Admin"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var state = await ctx.ActivityDefinitionReconciliationStates
                .SingleOrDefaultAsync(s => s.ActivityDefinitionId == defId);
            Assert.Null(state);
        }
    }

    [Fact]
    public void IsStale_IndexIsRegistered_OnReconciliationStateEntity()
    {
        using var host = ActivitiesDesignTestHost.Create();
        using var ctx = host.CreateContext();

        var entityType = ctx.Model.FindEntityType(typeof(ActivityDefinitionReconciliationState))!;
        var indexes = entityType.GetIndexes().ToList();

        Assert.Contains(indexes, i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ActivityDefinitionReconciliationState.IsStale));
    }
}
