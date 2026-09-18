using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Proves the claim the schema option rests on: migrations scaffolded without a schema apply into a configured
/// one, on the providers that have schemas, and a store operation round-trips there afterwards.
/// </summary>
/// <remarks>
/// The schema is never pre-created here, because creating it is part of what applying must do. SQLite has no
/// schemas and is covered by the unit tests instead.
/// </remarks>
public sealed class ModuleSchemaMigrationTests
{
    private const string Schema = "elsa_alt";

    [SkippableFact]
    public Task Every_module_installs_into_a_custom_schema_on_postgresql() => RunAsync("PostgreSql");

    [SkippableFact]
    public Task Every_module_installs_into_a_custom_schema_on_sql_server() => RunAsync("SqlServer");

    /// <summary>
    /// MySQL refuses the schema setting, because a MySQL schema is a database. What a MySQL host does instead is
    /// name that database in its connection string, so this leg proves the documented alternative, not the setting.
    /// </summary>
    [SkippableFact]
    public Task MySql_puts_every_module_in_the_database_its_connection_string_names() =>
        ProviderDatabase.RunAsync("MySql", async connection =>
        {
            Assert.Contains("elsa", connection, StringComparison.Ordinal);
            await ModuleContextCatalog.InstallAllAsync("MySql", connection);
            await RoundTripsAsync("MySql", connection, schema: null);
        });

    private static Task RunAsync(string provider) => ProviderDatabase.RunAsync(provider, async connection =>
    {
        await ModuleContextCatalog.InstallAllAsync(provider, connection, Schema);
        await RoundTripsAsync(provider, connection, Schema);
        await LeavesTheDefaultSchemaEmptyAsync(provider, connection);
    });

    private static async Task RoundTripsAsync(string provider, string connection, string? schema)
    {
        var type = ContextType(provider);
        var id = $"round-trip-{Guid.NewGuid():N}";
        await using (var context = (StudioPreferencesDbContext)ModuleContextCatalog.Create(type, connection, schema: schema))
        {
            context.Preferences.Add(new StudioPreferenceRecord
            {
                Id = id,
                SubjectId = "subject",
                TenantId = "tenant",
                StudioHostId = "host",
                Namespace = "namespace",
                SchemaVersion = 1,
                ValueJson = """{"theme":"stone"}""",
                UpdatedAt = DateTimeOffset.UnixEpoch,
                Revision = 1
            });
            await context.SaveChangesAsync();
        }

        await using var reader = (StudioPreferencesDbContext)ModuleContextCatalog.Create(type, connection, schema: schema);
        var stored = await reader.Preferences.OrderBy(record => record.Id).SingleAsync(record => record.Id == id);
        Assert.Equal("""{"theme":"stone"}""", stored.ValueJson);
    }

    /// <summary>
    /// The same context bound to no schema reads the provider's default one, where nothing was created: every
    /// migration is still pending there. That is the isolation an operator buys the setting for.
    /// </summary>
    private static async Task LeavesTheDefaultSchemaEmptyAsync(string provider, string connection)
    {
        await using var unqualified = ModuleContextCatalog.Create(ContextType(provider), connection);
        Assert.Equal(
            unqualified.Database.GetMigrations().Order(StringComparer.Ordinal),
            (await unqualified.Database.GetPendingMigrationsAsync()).Order(StringComparer.Ordinal));
    }

    private static Type ContextType(string provider) => ModuleContextCatalog.Contexts(provider)
        .Single(candidate => candidate.Name.StartsWith("StudioPreferences", StringComparison.Ordinal));
}
