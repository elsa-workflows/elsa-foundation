using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Activities.Design.Tests.Integration;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityDefinitionLookupTests
{
    [Fact]
    public async Task ListDefinitions_SearchTerm_MatchesActivityTypeKey()
    {
        using var host = ActivitiesDesignTestHost.Create();

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = "write-line",
                ActivityTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine",
                Category = "Primitives",
                DisplayName = null,
                Description = null
            });
            await ctx.SaveChangesAsync();
        }

        var lookup = new ActivityDefinitionLookup(
            new ThrowingActivityDefinitionVersionStore(),
            new ThrowingActivityDefinitionStore(),
            new TestDbContextFactory(host));

        var results = await lookup.ListDefinitions(searchTerm: "WriteLine");

        var result = Assert.Single(results);
        Assert.Equal("write-line", result.Id);
    }
}
