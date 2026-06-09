using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Sqlite;

public class SqliteDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext>
    where TDbContext : DbContext
{
    protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString)
    {
        builder.UseElsaSqlite(GetType().Assembly, connectionString);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
    }
}
