using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests;

/// <summary>
/// Applies a scripted artifact the way a DBA pipeline actually would: a plain ADO client for the target
/// engine, never EF Core's own <c>IMigrator</c>, which never sees the idempotent text form this leg checks
/// at all (issue #1875). Each engine gets the handling the issue calls for: SQL Server's script is split on
/// <c>GO</c> before <c>SqlConnection</c> sees any of it, and MySQL's is split honouring <c>DELIMITER</c>.
/// PostgreSQL needs neither split: the idempotent generator emits its own <c>START TRANSACTION;</c> /
/// <c>COMMIT;</c> pair per file, and Npgsql's simple query protocol sends multi-statement text -- including
/// those explicit transaction-control statements -- to the server as PostgreSQL's own <c>psql -f</c> would.
/// Wrapping the call in a second, client-side transaction would only fight the script's own one.
/// </summary>
internal static class RawScriptApplication
{
    public static Task ApplyAsync(string provider, string connectionString, IReadOnlyList<string> scripts) => provider switch
    {
        "PostgreSql" => ApplyPostgreSqlAsync(connectionString, scripts),
        "SqlServer" => ApplySqlServerAsync(connectionString, scripts),
        "MySql" => ApplyMySqlAsync(connectionString, scripts),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No raw client for this provider.")
    };

    private static async Task ApplyPostgreSqlAsync(string connectionString, IReadOnlyList<string> scripts)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in scripts)
        {
            await using var command = new NpgsqlCommand(script, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplySqlServerAsync(string connectionString, IReadOnlyList<string> scripts)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in scripts)
            foreach (var batch in SqlScriptSplitting.SplitSqlServerBatches(script))
            {
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
    }

    private static async Task ApplyMySqlAsync(string connectionString, IReadOnlyList<string> scripts)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in scripts)
            foreach (var statement in SqlScriptSplitting.SplitMySqlStatements(script))
            {
                await using var command = new MySqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync();
            }
    }
}
