using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Activities.Design.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;
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

        var dbContextFactory = new TestDbContextFactory(host);
        var definitionStore = new EFCoreActivityDefinitionStore(dbContextFactory, new ServiceCollection().BuildServiceProvider());
        var lookup = new ActivityDefinitionLookup(
            new ThrowingActivityDefinitionVersionStore(),
            definitionStore);

        var results = await lookup.ListDefinitions(searchTerm: "WriteLine");

        var result = Assert.Single(results);
        Assert.Equal("write-line", result.Id);
    }

    /// <summary>
    /// Covers issue #392: <c>tenantAgnostic</c> must reach <see cref="ActivityDefinitionFilter.TenantAgnostic"/>,
    /// which is what makes the store ignore the tenant scope.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task ListDefinitions_ForwardsTenantAgnosticToTheFilter(bool? tenantAgnostic)
    {
        var definitionStore = new RecordingActivityDefinitionStore();
        var lookup = new ActivityDefinitionLookup(new ThrowingActivityDefinitionVersionStore(), definitionStore);

        await lookup.ListDefinitions(tenantAgnostic: tenantAgnostic);

        Assert.NotNull(definitionStore.LastFilter);
        Assert.Equal(tenantAgnostic, definitionStore.LastFilter!.TenantAgnostic);
    }

    private sealed class RecordingActivityDefinitionStore : IActivityDefinitionStore
    {
        public ActivityDefinitionFilter? LastFilter { get; private set; }

        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<ActivityDefinition>>([]);
        }

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");
    }
}
