using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignScopeIdentityModelTests
{
    [Fact]
    public void Sqlite_model_uses_scoped_hashed_keys_for_every_tenant_aware_entity()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new ActivitiesDesignSqliteDbContext(options);

        foreach (var type in db.Model.GetEntityTypes().Where(x => !x.IsOwned() && !x.ClrType.IsAbstract))
        {
            var key = type.FindPrimaryKey()!;
            if (type.ClrType == typeof(ActivityAvailabilitySettingsRecord))
            {
                Assert.Equal(["TenantScopeKey", "ScopeIdentityHash"], key.Properties.Select(x => x.Name));
                Assert.Equal(64, type.FindProperty("ScopeIdentityHash")!.GetMaxLength());
            }
            else
            {
                Assert.Equal(["TenantScopeKey", "IdIdentityHash"], key.Properties.Select(x => x.Name));
                Assert.False(type.FindProperty("TenantScopeKey")!.IsNullable);
                Assert.False(type.FindProperty("IdIdentityHash")!.IsNullable);
                Assert.Equal(66, type.FindProperty("TenantScopeKey")!.GetMaxLength());
                Assert.Equal(64, type.FindProperty("IdIdentityHash")!.GetMaxLength());
            }

            Assert.Equal(450, type.FindProperty("Id")!.GetMaxLength());
        }

        foreach (var index in db.Model.GetEntityTypes().SelectMany(x => x.GetIndexes()))
        {
            var indexedStringLength = index.Properties
                .Where(x => x.ClrType == typeof(string))
                .Sum(x => x.GetMaxLength() ?? 450);
            Assert.True(indexedStringLength <= 450, $"Index {index.DeclaringEntityType.ClrType.Name}.{index.Name} exceeds the provider-neutral 450-character budget.");
        }

        var version = db.Model.FindEntityType(typeof(ActivityDefinitionVersion))!;
        Assert.Null(version.FindNavigation(nameof(ActivityDefinitionVersion.Definition)));

        foreach (var (type, property) in new[]
                 {
                     (typeof(ActivityDefinitionVersion), "DefinitionId"),
                     (typeof(ActivityDefinitionAuthoringState), "DefinitionId"),
                     (typeof(ActivityDefinitionDraft), "DefinitionId"),
                     (typeof(ActivityDefinitionDraftLayout), "DraftId"),
                     (typeof(ActivityDefinitionVersionLayout), "DefinitionVersionId"),
                     (typeof(ActivityDefinitionVersionPublication), "DefinitionVersionId"),
                     (typeof(ActivityDefinitionVersionPublication), "DefinitionId"),
                     (typeof(ActivityDependencyEdge), "OwnerVersionId"),
                     (typeof(ActivityDependencyEdge), "DependencyVersionId")
                 })
            Assert.Equal(450, db.Model.FindEntityType(type)!.FindProperty(property)!.GetMaxLength());
    }

    [Fact]
    public async Task Sqlite_allows_same_full_id_in_distinct_scopes_and_preserves_450_characters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var first = new ActivitiesDesignSqliteDbContext(options);
        await first.Database.EnsureCreatedAsync();

        var id = new string('x', 450);
        first.ActivityDefinitions.AddRange(
            Definition(id, "tenant-a", "type-a"),
            Definition(id, "tenant-b", "type-b"),
            Definition(id, null, "type-global"));
        first.ActivityDefinitionVersions.AddRange(
            Version(id, id, "tenant-a"),
            Version(id, id, "tenant-b"),
            Version(id, id, null));
        first.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = "authoring-450",
            TenantId = "tenant-a",
            DefinitionId = id,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "design")
        });
        first.ActivityDefinitionDraftLayouts.Add(new ActivityDefinitionDraftLayout
        {
            Id = "layout-450",
            TenantId = "tenant-a",
            DraftId = id,
            Revision = 1
        });
        first.ActivityDefinitionVersionLayouts.Add(new ActivityDefinitionVersionLayout
        {
            Id = "version-layout-450",
            TenantId = "tenant-a",
            DefinitionVersionId = id
        });
        var availabilityEntry = first.Entry(new ActivityAvailabilitySettingsRecord { Id = id, Scope = id });
        availabilityEntry.Property("TenantScopeKey").CurrentValue = ActivitiesDesignDbContext.GlobalTenantKey;
        availabilityEntry.State = EntityState.Added;
        await first.SaveChangesAsync();

        var tenants = await first.ActivityDefinitions.OrderBy(x => x.TenantId).Select(x => x.TenantId).ToArrayAsync();
        Assert.Equal(3, tenants.Length);
        Assert.Null(tenants[0]);
        Assert.Equal("tenant-a", tenants[1]);
        Assert.Equal("tenant-b", tenants[2]);
        var tenantDefinition = await first.ActivityDefinitions.SingleAsync(x => x.TenantId == "tenant-a");
        Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a"), first.Entry(tenantDefinition).Property<string>("TenantScopeKey").CurrentValue);
        Assert.Equal(ActivitiesDesignDbContext.ComputeIdentityHash(id), first.Entry(tenantDefinition).Property<string>("IdIdentityHash").CurrentValue);
        Assert.All(await first.ActivityDefinitions.Select(x => x.Id).ToArrayAsync(), value => Assert.Equal(450, value.Length));
        Assert.Equal(3, await first.ActivityDefinitionVersions.CountAsync());
        Assert.Equal(id, await first.ActivityDefinitionAuthoringStates.Select(x => x.DefinitionId).SingleAsync());
        Assert.Equal(id, await first.ActivityDefinitionDraftLayouts.Select(x => x.DraftId).SingleAsync());
        Assert.Equal(id, await first.ActivityDefinitionVersionLayouts.Select(x => x.DefinitionVersionId).SingleAsync());
        var availability = await first.ActivityAvailabilitySettings.SingleAsync();
        Assert.Equal(id, availability.Scope);
        Assert.Equal(ActivitiesDesignDbContext.ComputeIdentityHash(id), first.Entry(availability).Property<string>("ScopeIdentityHash").CurrentValue);

        await using var duplicate = new ActivitiesDesignSqliteDbContext(options);
        duplicate.ActivityDefinitions.Add(Definition(id, "tenant-a", "type-a-duplicate"));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    private static ActivityDefinition Definition(string id, string? tenantId, string activityTypeKey) => new()
    {
        Id = id,
        TenantId = tenantId,
        ActivityTypeKey = activityTypeKey,
        Category = "Tests",
        DisplayName = "Scope identity test"
    };

    private static ActivityDefinitionVersion Version(string id, string definitionId, string? tenantId) =>
        new("1.0.0", definitionId)
        {
            Id = id,
            TenantId = tenantId,
            ProviderKey = "provider",
            ProviderSchemaVersion = "1",
            ConsumerKey = "consumer",
            ConsumerSchemaVersion = "1",
            SourceKind = "test",
            SourceId = "source"
        };
}
