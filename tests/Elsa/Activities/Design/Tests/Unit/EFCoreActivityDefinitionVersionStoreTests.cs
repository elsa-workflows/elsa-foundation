using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// First direct coverage of <see cref="EFCoreActivityDefinitionVersionStore"/> over the real SQLite
/// in-memory host. Focused on the not-found convention: both <c>GetAsync</c> and
/// <c>GetWithDefinitionAsync</c> must throw <see cref="EntityNotFoundException"/> for a missing id so
/// the 404-mapping infrastructure (and <c>ActivityDefinitionLookup.FindVersion</c>) sees the one
/// exception type it expects (issue #417 item 8).
/// </summary>
public sealed class EFCoreActivityDefinitionVersionStoreTests
{
    [Fact]
    public async Task GetWithDefinition_throws_EntityNotFound_when_absent()
    {
        using var host = ActivitiesDesignTestHost.Create();
        var store = new EFCoreActivityDefinitionVersionStore(new HostDbContextFactory(host), host.Services);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetWithDefinitionAsync("missing"));
    }

    [Fact]
    public async Task GetWithDefinition_loads_owning_definition()
    {
        using var host = ActivitiesDesignTestHost.Create();
        var store = new EFCoreActivityDefinitionVersionStore(new HostDbContextFactory(host), host.Services);

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition { Id = "def1", ActivityTypeKey = "Acme.Send", Category = "General" });
            ctx.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion("1.0.0", "def1")
            {
                Id = "v1",
                DescriptorType = "Acme.SendActivity",
                SourceKind = "Json",
                SourceId = "asset-1"
            });
            await ctx.SaveChangesAsync();
        }

        var result = await store.GetWithDefinitionAsync("v1");

        Assert.NotNull(result.Definition);
        Assert.Equal("def1", result.Definition!.Id);
        Assert.Equal("Acme.Send", result.Definition.ActivityTypeKey);
    }

    /// <summary>
    /// Test-only <see cref="IDbContextFactory{TContext}"/> over the host's shared SQLite connection —
    /// the store creates its context through the factory, so every created context sees the same
    /// in-memory database.
    /// </summary>
    private sealed class HostDbContextFactory(ActivitiesDesignTestHost host) : IDbContextFactory<ActivitiesDesignDbContext>
    {
        public ActivitiesDesignDbContext CreateDbContext() => host.CreateContext();
    }
}
