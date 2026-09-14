using System.Data.Common;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MySql.Data.MySqlClient;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

internal static class MySqlTestContext
{
    public static SecretsMySqlDbContext Create(
        string connectionString,
        bool includePendingModel = false,
        string? migrationsAssembly = null) =>
        new(BuildOptions(connectionString, migrationsAssembly), includePendingModel);

    public static SecretsMySqlDbContext Create(
        DbConnection connection,
        bool includePendingModel = false,
        string? migrationsAssembly = null) =>
        new(BuildOptions(connection, migrationsAssembly), includePendingModel);

    public static MySqlConnectionStringBuilder Parse(string connectionString) =>
        new(connectionString);

    private static DbContextOptions<SecretsMySqlDbContext> BuildOptions(string connectionString, string? migrationsAssembly) =>
        new DbContextOptionsBuilder<SecretsMySqlDbContext>()
            .UseMySQL(connectionString, mySql => ConfigureMigrations(mySql, migrationsAssembly))
            .ReplaceService<IModelCacheKeyFactory, MySqlTestModelCacheKeyFactory>()
            .Options;

    private static DbContextOptions<SecretsMySqlDbContext> BuildOptions(DbConnection connection, string? migrationsAssembly) =>
        new DbContextOptionsBuilder<SecretsMySqlDbContext>()
            .UseMySQL(connection, mySql => ConfigureMigrations(mySql, migrationsAssembly))
            .ReplaceService<IModelCacheKeyFactory, MySqlTestModelCacheKeyFactory>()
            .Options;

    private static void ConfigureMigrations(
        global::MySql.EntityFrameworkCore.Infrastructure.MySQLDbContextOptionsBuilder mySql,
        string? migrationsAssembly)
    {
        mySql
            .MigrationsAssembly(migrationsAssembly ?? typeof(SecretsMySqlDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(SecretsMySqlDbContext.HistoryTableName);
    }
}
