using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.Fixtures.MigratingModule;

/// <summary>
/// The module's base context. It maps nothing: its migrations create and change a table by name, which needs no model
/// snapshot, and a context with no entities has no model to differ from one.
/// </summary>
public abstract class MigratingModuleDbContext(DbContextOptions options) : DbContext(options);

/// <summary>The SQLite-derived context, the one provider the fixture binds.</summary>
public sealed class MigratingModuleSqliteDbContext(DbContextOptions<MigratingModuleSqliteDbContext> options) : MigratingModuleDbContext(options);
