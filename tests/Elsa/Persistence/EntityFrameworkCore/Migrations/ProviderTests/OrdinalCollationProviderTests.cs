using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// What actually proves elsa-workflows/elsa-foundation#1837 is fixed: a real server, a database whose
/// default collation is linguistic, and the question the application asks — does <c>ORDER BY</c> on a key
/// column agree with <see cref="StringComparer.Ordinal"/>?
/// <para>
/// And #1855 with it: the change tracker has to agree with that schema, so two rows the primary key
/// separates are written as two rows through a real context, not merged before <c>SaveChanges</c> is
/// reached. A comparer read out of the model would not show that; this writes and reads back.
/// </para>
/// <para>
/// SQLite cannot show any of this. Its only TEXT collation is <c>BINARY</c> and it is the default, so the
/// SQLite legs are green whether or not the declaration exists. These two legs are the evidence.
/// </para>
/// </summary>
public sealed class OrdinalCollationProviderTests
{
    /// <summary>
    /// Keys that a linguistic collation reorders and, for the case pair, treats as equal: ordinal puts every
    /// uppercase letter before every lowercase one and sorts the accented letter far above both.
    /// </summary>
    private static readonly string[] Keys = ["B", "A", "a", "b", "Á", "_"];

    [SkippableFact]
    public Task Sql_server_orders_key_columns_ordinally_in_a_case_insensitive_database() =>
        ProviderDatabase.RunAsync("SqlServer", RunSqlServerAsync);

    [SkippableFact]
    public Task PostgreSql_orders_key_columns_ordinally_in_a_linguistic_database() =>
        ProviderDatabase.RunAsync("PostgreSql", RunPostgreSqlAsync);

    /// <summary>
    /// MySQL has its own channel, and it is the one that fails quietly. Oracle's provider reads
    /// <c>MySQL:Collation</c> out of a migration's target model and ignores the relational column collation
    /// entirely, so a module that declares only the relational one generates a migration that looks right
    /// in every file and produces columns on the server's default collation. This reads the server.
    /// </summary>
    [SkippableFact]
    public Task MySql_key_columns_carry_the_binary_collation_the_model_declares() =>
        ProviderDatabase.RunAsync("MySql", RunMySqlAsync);

    private static async Task RunSqlServerAsync(string connectionString)
    {
        // A stock SQL Server installs with a CI_AS default, and every uncollated column inherits it. This
        // is the environment the bug is invisible in and the fix is visible in.
        var hostile = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "elsa_ordinal_ci" }.ConnectionString;
        await using (var admin = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, "CREATE DATABASE [elsa_ordinal_ci] COLLATE Latin1_General_CI_AS");
        }

        await ModuleContextCatalog.InstallAllAsync("SqlServer", hostile);
        await using var connection = new SqlConnection(hostile);
        await connection.OpenAsync();

        // The control: without the declaration those columns would be CI_AS, and CI_AS is not ordinal here.
        Assert.NotEqual(Ordinal(), await OrderedAsync(connection, Literals("N"), "value COLLATE Latin1_General_CI_AS"));
        Assert.Equal(Ordinal(), await OrderedAsync(connection, Literals("N"), $"value COLLATE {EfOrdinalCollation.SqlServer}"));

        await AssertDeclaredCollationsReachedTheServerAsync("SqlServer", hostile, connection, """
            SELECT c.collation_name
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name = @table AND c.name = @column
            """);
        await AssertSecretNamesOrderOrdinallyAsync(connection, name => $"[{name}]", "@payload");
        await AssertCaseDistinctSecretsAreTwoRowsAsync("SqlServer", hostile, connection, name => $"[{name}]");
    }

    private static async Task RunMySqlAsync(string connectionString)
    {
        await ModuleContextCatalog.InstallAllAsync("MySql", connectionString);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        // MySQL's default for utf8mb4 is the accent- and case-insensitive collation, which is what a
        // column falls back to when the declaration does not reach it.
        Assert.Equal(
            "utf8mb4_0900_ai_ci",
            await ScalarAsync(connection, "SELECT DEFAULT_COLLATION_NAME FROM information_schema.schemata WHERE SCHEMA_NAME = DATABASE()"));

        await AssertDeclaredCollationsReachedTheServerAsync("MySql", connectionString, connection, """
            SELECT collation_name FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column
            """);
    }

    private static async Task RunPostgreSqlAsync(string connectionString)
    {
        // PostgreSQL's default collation follows the cluster locale, which on a real deployment is a
        // language locale rather than C. ICU's root locale is that case, spelled portably.
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, """
                CREATE DATABASE elsa_ordinal_icu
                TEMPLATE template0 ENCODING 'UTF8'
                LOCALE_PROVIDER icu ICU_LOCALE 'und' LC_COLLATE 'C' LC_CTYPE 'C'
                """);
        }

        var hostile = new NpgsqlConnectionStringBuilder(connectionString) { Database = "elsa_ordinal_icu" }.ConnectionString;
        await ModuleContextCatalog.InstallAllAsync("PostgreSql", hostile);
        await using var connection = new NpgsqlConnection(hostile);
        await connection.OpenAsync();

        Assert.NotEqual(Ordinal(), await OrderedAsync(connection, Literals(""), "value COLLATE \"und-x-icu\""));
        Assert.Equal(Ordinal(), await OrderedAsync(connection, Literals(""), $"value COLLATE \"{EfOrdinalCollation.PostgreSql}\""));

        await AssertDeclaredCollationsReachedTheServerAsync("PostgreSql", hostile, connection, """
            SELECT collation_name FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """);
        // Secrets stores its payload as jsonb on PostgreSQL, and a text parameter needs saying so.
        await AssertSecretNamesOrderOrdinallyAsync(connection, name => $"\"{name}\"", "CAST(@payload AS jsonb)");
        await AssertCaseDistinctSecretsAreTwoRowsAsync("PostgreSql", hostile, connection, name => $"\"{name}\"");
    }

    /// <summary>
    /// Every column each in-scope module declares ordinal carries that collation on the live server. This is
    /// the assertion that bites when a column is dropped from a module's list and the migration regenerated:
    /// the catalog reports the database's default instead.
    /// </summary>
    private static async Task AssertDeclaredCollationsReachedTheServerAsync(
        string provider, string connectionString, DbConnection connection, string catalogQuery)
    {
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(provider));
        foreach (var type in ModuleContextCatalog.Contexts(provider)
                     .Where(ModuleContextCatalog.DeclaresOrdinal))
        {
            await using var context = ModuleContextCatalog.Create(type, connectionString);
            var columns = 0;
            // Collation lives in the design-time model; the read-optimized one throws rather than answering.
            foreach (var entity in context.GetService<IDesignTimeModel>().Model.GetEntityTypes())
            {
                if (entity.GetTableName() is not { } table)
                    continue;
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties().Where(property => property.GetCollation(store) is not null))
                {
                    var column = property.GetColumnName(store)!;
                    Assert.Equal(expected, await ScalarAsync(connection, catalogQuery, ("@table", table), ("@column", column)));
                    columns++;
                }
            }

            // A module that stopped declaring anything would otherwise pass this loop vacuously.
            Assert.True(columns > 0, $"{type.Name} declares no ordinal column at all.");
        }
    }

    /// <summary>
    /// The application-level question, on a real key column of a real module table: a page the database
    /// orders has to be the page an ordinal cursor expects.
    /// <para>
    /// The rows go in through raw SQL rather than through the context, so that what is being measured is the
    /// server's ordering and nothing else. Whether a context can write them at all is
    /// <see cref="AssertCaseDistinctSecretsAreTwoRowsAsync"/>, which runs after this one and under its own
    /// tenant, because this assertion reads every row in the table.
    /// </para>
    /// </summary>
    private static async Task AssertSecretNamesOrderOrdinallyAsync(DbConnection connection, Func<string, string> quote, string payload)
    {
        string[] columns =
        [
            "TenantId", "NormalizedName", "NameSearchKey", "DisplayNameSearchKey",
            "TypeNameLookupKey", "StoreNameLookupKey", "Status", "HasNonExpiringActiveVersion",
            "Payload", "ConcurrencyToken"
        ];
        foreach (var key in Keys)
            await ExecuteAsync(
                connection,
                $"INSERT INTO {quote("elsa_secrets")} ({string.Join(", ", columns.Select(quote))}) " +
                $"VALUES (@tenant, @key, @key, @key, @key, @key, @status, @active, {payload}, @token)",
                ("@tenant", "t"), ("@key", key), ("@status", "Active"),
                ("@active", false), ("@payload", "{}"), ("@token", new byte[] { 0 }));

        var column = quote("NormalizedName");
        Assert.Equal(Ordinal(), await ReadAsync(connection, $"SELECT {column} FROM {quote("elsa_secrets")} ORDER BY {column}"));
    }

    /// <summary>
    /// #1855, end to end on a live server: two secrets whose <c>NormalizedName</c> differs only in case go in
    /// through the module's own context and come back as two rows.
    /// <para>
    /// Both halves have to hold for this to pass. If the change tracker compares the key case-insensitively
    /// the second <c>Add</c> throws before any SQL is sent; if the column lost its binary collation the insert
    /// is refused by the primary key. Under its own tenant, so the table-wide ordering assertion above is
    /// unaffected.
    /// </para>
    /// </summary>
    private static async Task AssertCaseDistinctSecretsAreTwoRowsAsync(
        string provider, string connectionString, DbConnection connection, Func<string, string> quote)
    {
        const string tenant = "tracker";
        await using (var context = ModuleContextCatalog.Create(
                         ModuleContextCatalog.Contexts(provider).Single(type => type.Name.StartsWith("Secrets", StringComparison.Ordinal)),
                         connectionString))
        {
            context.Add(new SecretRecord { TenantId = tenant, NormalizedName = "A", Status = "Active" });
            context.Add(new SecretRecord { TenantId = tenant, NormalizedName = "a", Status = "Active" });
            await context.SaveChangesAsync();
        }

        Assert.Equal(
            ["A", "a"],
            await ReadAsync(
                connection,
                $"SELECT {quote("NormalizedName")} FROM {quote("elsa_secrets")} " +
                $"WHERE {quote("TenantId")} = @tenant ORDER BY {quote("NormalizedName")}",
                ("@tenant", tenant)));
    }

    private static string[] Ordinal() => [.. Keys.Order(StringComparer.Ordinal)];

    /// <summary>The probe keys as a literal table, so the control needs no schema of its own.</summary>
    private static string Literals(string prefix) =>
        $"SELECT value FROM (VALUES {string.Join(", ", Keys.Select(key => $"({prefix}'{key}')"))}) AS probe(value)";

    private static async Task<string[]> OrderedAsync(DbConnection connection, string source, string orderBy) =>
        await ReadAsync(connection, $"{source} ORDER BY {orderBy}");

    private static async Task<string[]> ReadAsync(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return [.. values];
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static DbCommand Command(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
