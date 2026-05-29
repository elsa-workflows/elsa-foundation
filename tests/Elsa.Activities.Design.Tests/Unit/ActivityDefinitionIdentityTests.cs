using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US1 — Logical activity identity survives normal refactors.
/// </summary>
public sealed class ActivityDefinitionIdentityTests
{
    [Fact]
    public async Task ActivityTypeKey_SourceKind_SourceId_ReconciledAt_AreImmutable_AfterInsert()
    {
        using var host = ActivitiesDesignTestHost.Create();

        var id = Guid.NewGuid().ToString("N");
        var initialReconciledAt = new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = id,
                ActivityTypeKey = "Foo",
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = initialReconciledAt,
                ReconciledBy = "test",
                Category = "Test"
            });
            await ctx.SaveChangesAsync();
        }

        await AssertImmutable(host, c => GetDefinitionEntryProperty(c, id, nameof(ActivityDefinition.ReconciledAt)).CurrentValue = initialReconciledAt.AddDays(1));
        await AssertImmutable(host, c => GetDefinitionEntryProperty(c, id, nameof(ActivityDefinition.ReconciledBy)).CurrentValue = "Foo");
        await AssertImmutable(host, c => GetDefinitionEntryProperty(c, id, nameof(ActivityDefinition.SourceKind)).CurrentValue = "Bar");
        await AssertImmutable(host, c => GetDefinitionEntryProperty(c, id, nameof(ActivityDefinition.SourceId)).CurrentValue = "Bar");
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
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test",
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
    public async Task DuplicateIdentityTuple_ThrowsOnInsert()
    {
        using var host = ActivitiesDesignTestHost.Create();

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityTypeKey = "Foo",
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test",
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
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test",
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
