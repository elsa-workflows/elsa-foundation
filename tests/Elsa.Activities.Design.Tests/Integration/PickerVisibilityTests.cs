using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Activities.Design.Tests.Unit;
using Elsa.Persistence.Core;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US2 — Picker = catalog visibility. Under Model X (Unit C 2026-05-28), visibility is
/// catalog membership; there is no per-row removal mechanism at the reconciliation layer
/// (source disappearance is intentionally not tracked — versions are never deleted).
/// Context-aware visibility (tenant / role / feature-flag) is a separate policy layer
/// per §E2.8.
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
    public async Task CatalogRow_IsAlwaysVisible_UnderModelX()
    {
        // Under Model X, every persisted catalog row is visible. There is no per-row
        // removal flag and no reconciliation-state sibling to LEFT JOIN against.
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
