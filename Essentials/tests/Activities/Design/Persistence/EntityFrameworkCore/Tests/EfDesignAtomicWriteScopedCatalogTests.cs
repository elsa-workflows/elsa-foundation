using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class EfDesignAtomicWriteScopedCatalogTests
{
    [Fact]
    public async Task Sqlite_scoped_default_access_accepts_host_wide_catalog_create()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var store = CreateStore(db);

        var created = await store.Execute(
            new DesignOperationKey("host-wide-create"),
            Definition("global", tenant: null),
            Version("global", "global-v1", tenant: null));

        Assert.Equal("global", created.DefinitionId);
        db.ChangeTracker.Clear();
        var definition = await db.ActivityDefinitions.SingleAsync(x => x.Id == "global");
        var version = await db.ActivityDefinitionVersions.SingleAsync(x => x.Id == "global-v1");
        var marker = await db.ActivityDesignOperations.SingleAsync();
        Assert.Null(definition.TenantId);
        Assert.Null(version.TenantId);
        Assert.Equal(
            ActivitiesDesignDbContext.GlobalTenantKey,
            await db.ActivityDefinitions.Where(x => x.Id == "global").Select(x => EF.Property<string>(x, "TenantKey")).SingleAsync());
        Assert.Null(marker.TenantId);
        Assert.Equal(ActivitiesDesignDbContext.GlobalTenantKey, marker.TenantScopeKey);
    }

    [Fact]
    public async Task Sqlite_scoped_default_access_accepts_bound_tenant_create()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var store = CreateStore(db);

        var created = await store.Execute(
            new DesignOperationKey("bound-tenant-create"),
            Definition("bound", tenant: "default"),
            Version("bound", "bound-v1", tenant: "default"));

        Assert.Equal("bound", created.DefinitionId);
        db.ChangeTracker.Clear();
        var definition = await db.ActivityDefinitions.SingleAsync(x => x.Id == "bound");
        Assert.Equal("default", definition.TenantId);
        Assert.NotEqual(
            ActivitiesDesignDbContext.GlobalTenantKey,
            await db.ActivityDefinitions.Where(x => x.Id == "bound").Select(x => EF.Property<string>(x, "TenantKey")).SingleAsync());
        var marker = await db.ActivityDesignOperations.SingleAsync();
        Assert.Equal("default", marker.TenantId);
        Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("default"), marker.TenantScopeKey);
    }

    [Fact]
    public async Task Sqlite_scoped_default_access_rejects_a_different_tenant_create()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var store = CreateStore(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Execute(
            new DesignOperationKey("foreign-tenant-create"),
            Definition("foreign", tenant: "other"),
            Version("foreign", "foreign-v1", tenant: "other")));
        Assert.False(await db.ActivityDefinitions.AnyAsync());
    }

    private static IAddActivityDefinitionCommand CreateStore(ActivitiesDesignDbContext db)
    {
        var access = new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        return new EfActivityDesignStores(db, access, new EfDesignAtomicWrite(db, access));
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ActivitiesDesignSqliteDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);

    private static ActivityDefinition Definition(string id, string? tenant) =>
        new() { Id = id, TenantId = tenant, ActivityTypeKey = $"Acme.{id}", Category = "Tests" };

    private static ActivityDefinitionVersion Version(string definitionId, string id, string? tenant) =>
        new("1.0.0", definitionId)
        {
            Id = id,
            TenantId = tenant,
            ProviderKey = "provider",
            ProviderSchemaVersion = "1",
            ConsumerKey = "consumer",
            ConsumerSchemaVersion = "1",
            SourceKind = "test",
            SourceId = id
        };

    private sealed class TestAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
