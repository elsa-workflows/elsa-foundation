using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

internal static class IdentityEfProviderDatabaseProvisioning
{
    public static async Task EnsureModuleTablesAsync(
        DbContext context,
        string provider,
        params string[] tableNames)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (tableNames.Length == 0 || tableNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty module table name is required.", nameof(tableNames));

        if (await context.Database.EnsureCreatedAsync())
            return;

        var existence = new bool[tableNames.Length];
        for (var index = 0; index < tableNames.Length; index++)
            existence[index] = await TableExistsAsync(context, provider, tableNames[index]);

        if (existence.All(value => value))
            return;
        if (existence.Any(value => value))
            throw new InvalidOperationException(
                $"The {provider} test database contains only part of the expected module schema: " +
                string.Join(", ", tableNames.Zip(existence, (name, exists) => $"{name}={exists}")));

        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    private static async Task<bool> TableExistsAsync(DbContext context, string provider, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter)
            await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = provider switch
            {
                "PostgreSql" => "SELECT CASE WHEN to_regclass(@tableName) IS NULL THEN 0 ELSE 1 END",
                "SqlServer" => "SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END",
                "MySql" => "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @tableName",
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            };
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = provider == "PostgreSql" ? $"public.{tableName}" : tableName;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        finally
        {
            if (closeAfter)
                await connection.CloseAsync();
        }
    }
}
