using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>
/// The module's base context. It maps the finalization record every module context maps, and nothing else: the gate
/// is what this fixture exercises, not a store.
/// </summary>
public abstract class FeedModuleDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.MapSchemaFinalization(FeedModule.HistoryModuleName).IndexSchemaVersionStamps();
}

/// <summary>The SQLite-derived context, the provider the fixture binds by default.</summary>
public sealed class FeedModuleSqliteDbContext(DbContextOptions<FeedModuleSqliteDbContext> options) : FeedModuleDbContext(options);

/// <summary>The PostgreSQL-derived context, bound when the feature's <c>Provider</c> setting says so (#2151).</summary>
public sealed class FeedModulePostgreSqlDbContext(DbContextOptions<FeedModulePostgreSqlDbContext> options) : FeedModuleDbContext(options);
