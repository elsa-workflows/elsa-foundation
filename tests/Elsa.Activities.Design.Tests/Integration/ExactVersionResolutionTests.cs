using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US4 exact-resolution test (SC-006, FR-013). Resolution by <c>(DefinitionId, exact semver)</c>
/// returns the one record or none, and build-metadata-equivalent strings resolve as the same
/// logical version: a lookup for <c>1.0.0</c> resolves a row persisted as <c>1.0.0+build</c>.
/// The match runs DB-side via the filter's <c>SemVerSortKey</c> equality.
/// </summary>
public sealed class ExactVersionResolutionTests
{
    [Fact]
    public async Task ResolvesExistingVersion_AndReturnsNoneForAbsent()
    {
        using var host = ActivitiesDesignTestHost.Create();
        await Seed(host, "def-1", ["2.1.0", "3.0.0"]);

        Assert.Equal("2.1.0", await ResolveVersion(host, "def-1", "2.1.0"));
        Assert.Null(await Resolve(host, "def-1", "9.9.9"));
    }

    [Fact]
    public async Task BuildMetadataIsIgnored_WhenResolving()
    {
        using var host = ActivitiesDesignTestHost.Create();
        await Seed(host, "def-1", ["1.0.0+build"]);

        // The row is persisted verbatim as "1.0.0+build"; a lookup for the metadata-free "1.0.0"
        // resolves it, and the persisted string is returned unchanged.
        var resolved = await Resolve(host, "def-1", "1.0.0");
        Assert.NotNull(resolved);
        Assert.Equal("1.0.0+build", resolved!.Version);
    }

    private static async Task<ActivityDefinitionVersion?> Resolve(ActivitiesDesignTestHost host, string definitionId, string version)
    {
        await using var ctx = host.CreateContext();
        var filter = new ActivityDefinitionVersionFilter { DefinitionId = definitionId, Version = version };
        return await filter.Apply(ctx.Set<ActivityDefinitionVersion>()).SingleOrDefaultAsync();
    }

    private static async Task<string?> ResolveVersion(ActivitiesDesignTestHost host, string definitionId, string version) =>
        (await Resolve(host, definitionId, version))?.Version;

    private static async Task Seed(ActivitiesDesignTestHost host, string definitionId, string[] versions)
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
