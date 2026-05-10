using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

namespace Elsa.Persistence.EFCore.Extensions
{
    /// <summary>
    /// Provides options for configuring Elsa's Entity Framework Core integration.
    /// </summary>
    public static class ElsaDbContextOptionsExtensions
    {
        /// <summary>
        /// Installs a custom extension for Elsa's Entity Framework Core integration.
        /// </summary>
        /// <param name="optionsBuilder">The options builder to install the extension on.</param>
        /// <param name="options">The options to install.</param>
        public static DbContextOptionsBuilder UseElsaDbContextOptions(this DbContextOptionsBuilder optionsBuilder, ElsaDbContextOptions? options)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new ElsaDbContextOptionsExtension(options));
            optionsBuilder.ReplaceService<IMigrationsAssembly, DbSchemaAwareMigrationAssembly>();
            return optionsBuilder;
        }

        public static string GetMigrationsAssemblyName(this ElsaDbContextOptions? options, Assembly migrationsAssembly) => options?.MigrationsAssemblyName ?? migrationsAssembly.GetName().Name!;
        public static string GetMigrationsHistoryTableName(this ElsaDbContextOptions? options) => options?.MigrationsHistoryTableName ?? ElsaDbContextBase.MigrationsHistoryTable;
        public static string GetSchemaName(this ElsaDbContextOptions? options) => options?.SchemaName ?? ElsaDbContextBase.ElsaSchema;
    }
}
