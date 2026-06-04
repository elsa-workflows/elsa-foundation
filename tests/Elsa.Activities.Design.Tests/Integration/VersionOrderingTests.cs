using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.OrderDefinitions;
using Elsa.Activities.Design.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US3 ordering test (SC-002). Listings and "latest version" resolution order by SemVer
/// precedence, not lexical or numeric-counter order. The order is produced DB-side: the
/// <see cref="ActivityVersionOrderDefinition"/> sorts on the persisted <c>SemVerSortKey</c>
/// column, so the SQL <c>ORDER BY</c> runs in the database — not after client materialisation.
/// Multi-digit segments (<c>10.0.0 &gt; 2.0.0</c>, <c>1.0.10 &gt; 1.0.2</c>) are the cases that
/// distinguish precedence ordering from lexical string ordering.
/// </summary>
public sealed class VersionOrderingTests
{
    [Fact]
    public async Task ListingOrdersByPrecedence_MajorMinor_DescendingDbSide()
    {
        using var host = ActivitiesDesignTestHost.Create();
        await SeedDefinition(host, "def-major", ["1.0.0", "2.0.0", "10.0.0", "1.2.0"]);

        await using var ctx = host.CreateContext();
        var order = new ActivityVersionOrderDefinition();
        var query = ctx.Set<ActivityDefinitionVersion>()
            .Where(v => v.DefinitionId == "def-major")
            .OrderByDescending(order.KeySelector);

        // The ORDER BY runs DB-side on the persisted SemVerSortKey column, not after materialisation.
        var sql = query.ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains(nameof(ActivityDefinitionVersion.SemVerSortKey), sql);

        var ordered = (await query.ToListAsync()).Select(v => v.Version).ToList();
        Assert.Equal(new List<string> { "10.0.0", "2.0.0", "1.2.0", "1.0.0" }, ordered);
    }

    [Fact]
    public async Task ListingOrdersByPrecedence_Patch_MultiDigitDescendingDbSide()
    {
        using var host = ActivitiesDesignTestHost.Create();
        await SeedDefinition(host, "def-patch", ["1.0.1", "1.0.10", "1.0.2"]);

        await using var ctx = host.CreateContext();
        var order = new ActivityVersionOrderDefinition();
        var query = ctx.Set<ActivityDefinitionVersion>()
            .Where(v => v.DefinitionId == "def-patch")
            .OrderByDescending(order.KeySelector);

        var sql = query.ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains(nameof(ActivityDefinitionVersion.SemVerSortKey), sql);

        var ordered = (await query.ToListAsync()).Select(v => v.Version).ToList();
        // 1.0.10 is the highest patch — lexical ordering would wrongly place "1.0.2" above "1.0.10".
        Assert.Equal(new List<string> { "1.0.10", "1.0.2", "1.0.1" }, ordered);
    }

    private static async Task SeedDefinition(ActivitiesDesignTestHost host, string definitionId, string[] versions)
    {
        await using var ctx = host.CreateContext();

        ctx.Add(new ActivityDefinition
        {
            Id = definitionId,
            ActivityTypeKey = $"Acme.Activities.{definitionId}",
            Category = "Acme",
        });

        foreach (var version in versions)
            ctx.Add(new ActivityDefinitionVersion(version, definitionId)
            {
                Id = Guid.NewGuid().ToString("N"),
                ImplementationKind = "Clr",
                SourceKind = "CLR",
                SourceId = definitionId,
            });

        await ctx.SaveChangesAsync();
    }
}
