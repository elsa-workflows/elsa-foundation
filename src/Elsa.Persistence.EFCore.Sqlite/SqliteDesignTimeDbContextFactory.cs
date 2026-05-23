using Elsa.Persistence.EFCore.Sqlite.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Sqlite;

public class SqliteDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext>
    where TDbContext : DbContext
{
    protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString)
    {
        builder.UseElsaSqlite(GetType().Assembly, connectionString);
    }
}
