using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Activities.Design.Tests.Unit;
using Elsa.Persistence.Core;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US2 — Picker = catalog visibility. The picker query returns rows whose reconciliation-state
/// sibling does NOT mark them removed; no live-provider enumeration path exists.
/// </summary>
public sealed class PickerVisibilityTests
{
    [Fact]
    public async Task NoCatalogRow_NoCLRType_IsNeverVisible()
    {
        // Spec FR-007, SC-009: visibility is catalog presence, never live-provider scan.
        // With an empty catalog, the picker returns empty regardless of what CLR types
        // happen to be loaded in the process.
        using var host = ActivitiesDesignTestHost.Create();
        var lookup = CreateLookup(host);

        var result = await lookup.ListDefinitions(cancellationToken: CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CatalogRow_NoReconciliationSibling_IsVisible()
    {
        // Admin-created rows have no reconciliation-state sibling. They appear in the picker.
        using var host = ActivitiesDesignTestHost.Create();
        var lookup = CreateLookup(host);

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(NewDefinition("admin-1", "Admin.Created"));
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        var result = (await lookup.ListDefinitions(cancellationToken: CancellationToken.None)).ToList();

        Assert.Single(result);
        Assert.Equal("Admin.Created", result[0].ActivityTypeKey);
    }

    [Fact]
    public async Task CatalogRow_WithRemovedAtSibling_IsExcluded()
    {
        // A reconciliation-state sibling with RemovedAt set excludes the row from the picker.
        using var host = ActivitiesDesignTestHost.Create();
        var lookup = CreateLookup(host);

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(NewDefinition("def-1", "Live.One"));
            ctx.ActivityDefinitions.Add(NewDefinition("def-2", "Removed.One"));
            ctx.ActivityDefinitionReconciliationStates.Add(new ActivityDefinitionReconciliationState
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityDefinitionId = "def-2",
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
                LastProvisionedAt = DateTimeOffset.UtcNow.AddDays(-10),
                RemovedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        var result = (await lookup.ListDefinitions(cancellationToken: CancellationToken.None)).ToList();

        Assert.Single(result);
        Assert.Equal("Live.One", result[0].ActivityTypeKey);
    }

    [Fact]
    public async Task MixedSourceKinds_AllReturned_NoKindSpecificFiltering()
    {
        // The picker is kind-agnostic. CLR-sourced + non-CLR-sourced rows both appear.
        using var host = ActivitiesDesignTestHost.Create();
        var lookup = CreateLookup(host);

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(NewDefinition("clr-1", "Clr.A", "Json"));
            ctx.ActivityDefinitions.Add(NewDefinition("wf-1", "Workflow.A", "Workflow"));
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        var result = (await lookup.ListDefinitions(cancellationToken: CancellationToken.None)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.ActivityTypeKey == "Clr.A");
        Assert.Contains(result, x => x.ActivityTypeKey == "Workflow.A");
    }

    private static ActivityDefinition NewDefinition(string id, string activityTypeKey, string sourceKind = "Json") => new()
    {
        Id = id,
        ActivityTypeKey = activityTypeKey,
        SourceKind = sourceKind,
        SourceId = "Elsa.Test",
        ProvisionedAt = DateTimeOffset.UtcNow,
        ProvisionedBy = "test",
        Category = "Test"
    };

    private static ActivityDefinitionLookup CreateLookup(ActivitiesDesignTestHost host)
    {
        // Unit-level: stub the IQueries deps with throw-on-call shims since this test
        // class only exercises ListDefinitions (which goes directly through the context
        // factory). The other lookup methods aren't covered here.
        return new ActivityDefinitionLookup(
            new ThrowingQueries<ActivityDefinitionVersion>(),
            new ThrowingQueries<ActivityDefinition>(),
            new TestDbContextFactory(host));
    }
}
