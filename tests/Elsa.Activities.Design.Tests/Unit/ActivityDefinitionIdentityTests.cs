using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US1 — Logical activity identity survives normal refactors. After the
/// Definition-is-a-visual-shell refactor (2026-05-29), provenance fields
/// (SourceKind / SourceId / ReconciledAt / ReconciledBy) live on the version only;
/// the Definition's immutability surface is reduced to <c>ActivityTypeKey</c>.
/// </summary>
public sealed class ActivityDefinitionIdentityTests
{
    [Fact]
    public async Task ActivityTypeKey_IsImmutable_AfterInsert()
    {
        using var host = ActivitiesDesignTestHost.Create();

        var id = Guid.NewGuid().ToString("N");

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = id,
                ActivityTypeKey = "Foo",
                Category = "Test"
            });
            await ctx.SaveChangesAsync();
        }

        await AssertImmutable(host, c => GetDefinitionEntryProperty(c, id, nameof(ActivityDefinition.ActivityTypeKey)).CurrentValue = "Bar");
    }

    private static PropertyEntry GetDefinitionEntryProperty(ActivitiesDesignDbContext ctx, string id, string propertyName)
    {
        var entityEntry = ctx.Entry(
            ctx.Set<ActivityDefinition>().First(x => x.Id == id)
        );

        return entityEntry.Property(propertyName);
    }

    [Fact]
    public async Task Identity_SurvivesNewVersionWithDifferentTypeInfo()
    {
        using var host = ActivitiesDesignTestHost.Create();

        var defId = Guid.NewGuid().ToString("N");
        var v1Id = Guid.NewGuid().ToString("N");
        var v2Id = Guid.NewGuid().ToString("N");

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = defId,
                ActivityTypeKey = "Foo",
                Category = "Test"
            });
            ctx.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion(1, defId)
            {
                Id = v1Id,
                ImplementationKind = "Clr",
                ImplementationDescriptor = new Elsa.Activities.Design.Core.Models.ClrImplementationDescriptor(
                    new Elsa.Primitives.Models.TypeInformation("Foo", "Acme.X", "Acme.X", "1.0.0.0")),
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion(2, defId)
            {
                Id = v2Id,
                ImplementationKind = "Clr",
                ImplementationDescriptor = new Elsa.Activities.Design.Core.Models.ClrImplementationDescriptor(
                    new Elsa.Primitives.Models.TypeInformation("FooRenamed", "Acme.Y", "Acme.Y", "2.0.0.0")),
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var byTypeKey = await ctx.ActivityDefinitions.SingleAsync(x => x.ActivityTypeKey == "Foo");
            Assert.Equal(defId, byTypeKey.Id);

            var versions = await ctx.ActivityDefinitionVersions
                .Where(v => v.DefinitionId == defId)
                .OrderBy(v => v.Version)
                .ToListAsync();
            Assert.Equal(2, versions.Count);
            Assert.Equal("Clr", versions[0].ImplementationKind);
            Assert.Equal("Clr", versions[1].ImplementationKind);
        }
    }

    [Fact]
    public async Task DuplicateActivityTypeKey_ThrowsOnInsert()
    {
        using var host = ActivitiesDesignTestHost.Create();

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityTypeKey = "Foo",
                Category = "Test"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityTypeKey = "Foo",
                Category = "Other"
            });

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    private static async Task AssertImmutable(ActivitiesDesignTestHost host, Action<ActivitiesDesignDbContext> mutation)
    {
        await using var ctx = host.CreateContext();
        mutation(ctx);
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.SaveChangesAsync());
    }
}
