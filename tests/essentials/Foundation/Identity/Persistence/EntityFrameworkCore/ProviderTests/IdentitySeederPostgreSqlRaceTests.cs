using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(IdentityProviderPostgreSqlFixture.CollectionName)]
public sealed class IdentitySeederPostgreSqlRaceTests(IdentityProviderPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task Two_ef_seeders_both_succeed_when_both_pass_the_role_membership_check_on_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "PostgreSql is unavailable.");

        // Its own database: the provider smoke tests assert table-wide row counts in the fixture's database.
        var databaseName = $"identity_seeder_race_{Guid.NewGuid():N}";
        await ExecuteAsync(fixture.ConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName }.ConnectionString;
            await EfCoreIdentitySeederRace.RunAsync(
                new IdentityIamEntityFrameworkCoreOptions { Provider = "PostgreSql", ConnectionString = connectionString },
                context => context.Database.EnsureCreatedAsync(),
                iterations: 10);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(fixture.ConnectionString, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }
}
