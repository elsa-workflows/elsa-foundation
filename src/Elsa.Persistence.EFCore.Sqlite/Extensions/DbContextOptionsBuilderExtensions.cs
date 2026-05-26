using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Reflection;

namespace Elsa.Persistence.EFCore.Sqlite.Extensions
{
    /// <summary>
    /// Contains extension methods for <see cref="DbContextOptionsBuilder"/>.
    /// </summary>
    public static class DbContextOptionsBuilderExtensions
    {
        /// <summary>
        /// Configures Entity Framework Core with SQLite.
        /// </summary>
        public static DbContextOptionsBuilder UseElsaSqlite(this DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString = SqliteConstants.DefaultConnectionString, ElsaDbContextOptions? options = null, Action<SqliteDbContextOptionsBuilder>? configure = null)
        {
            builder
                .UseElsaDbContextOptions(options)
                .UseSqlite(connectionString, builder =>
                {
                    builder
                        .MigrationsAssembly(options.GetMigrationsAssemblyName(migrationsAssembly))
                        .MigrationsHistoryTable(options.GetMigrationsHistoryTableName(), options.GetSchemaName());

                    configure?.Invoke(builder);
                });

            return builder;
        }
    }
}
