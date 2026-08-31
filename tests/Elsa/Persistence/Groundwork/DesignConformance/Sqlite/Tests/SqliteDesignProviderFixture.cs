using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Kernel.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// T051: explicit provider-level close/reopen and schema-drift hooks for the SQLite leaf. The
/// contract suites already restart between phases; these hooks pin the provider promises directly:
/// closing and reopening the same database preserves admission and durable design state, and
/// physical index drift is admitted as degraded on both explicit revalidation and runtime
/// admission after reopen — never an empty store, never a fallback.
/// </summary>
public sealed class SqliteDesignProviderFixture
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    [Fact]
    public async Task Close_and_reopen_preserves_admission_and_durable_design_state()
    {
        await using var fixture = await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry);
        await fixture.ValidateReadinessAsync();
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            await scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("provider-reopen"),
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(),
                CancellationToken.None);
        }

        await fixture.RestartAsync();
        await fixture.ValidateReadinessAsync();

        using var reopened = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var definition = await reopened.ServiceProvider
            .GetRequiredService<IWorkflowDefinitionStore>()
            .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        Assert.NotNull(definition);
        Assert.Equal(1, fixture.RestartCount);
    }

    [Fact]
    public async Task Physical_index_drift_is_degraded_on_revalidation_and_restart()
    {
        await using var fixture = await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry);
        await fixture.ValidateReadinessAsync();
        var droppedIndex = await DropAppliedDesignIndexAsync(fixture.SqliteConnectionString);

        // Groundwork 0.4 keeps index-only drift serviceable but reports the affected declaration as
        // degraded, so dependent query shapes can refuse without blocking unrelated storage.
        await fixture.ValidateReadinessAsync();
        Assert.False(await IndexExistsAsync(fixture.SqliteConnectionString, droppedIndex));
        AssertDegradedWithoutBlockedUnits(fixture.InspectRuntimeAdmission());

        await fixture.RestartAsync();
        Assert.False(await IndexExistsAsync(fixture.SqliteConnectionString, droppedIndex));
        AssertDegradedWithoutBlockedUnits(fixture.InspectRuntimeAdmission());
    }

    private static async Task<string> DropAppliedDesignIndexAsync(string connectionString)
    {
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var find = connection.CreateCommand())
            {
                find.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'index' " +
                    "AND sql IS NOT NULL AND name LIKE '%definition%' ORDER BY name LIMIT 1";
                var droppedIndex = (string?)await find.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("No applied design index was found to drift.");
                await DropIndexAsync(connection, droppedIndex);
                return droppedIndex;
            }
        }
    }

    private static async Task DropIndexAsync(SqliteConnection connection, string indexName)
    {
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP INDEX \"{indexName}\"";
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IndexExistsAsync(string connectionString, string indexName)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var find = connection.CreateCommand();
        find.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name";
        find.Parameters.AddWithValue("$name", indexName);
        return (long)(await find.ExecuteScalarAsync() ?? 0L) == 1;
    }

    private static void AssertDegradedWithoutBlockedUnits(
        IReadOnlyList<GroundworkRuntimeSchemaAdmissionStatus> statuses)
    {
        Assert.Contains(GroundworkRuntimeSchemaAdmissionStatus.Degraded, statuses);
        Assert.DoesNotContain(GroundworkRuntimeSchemaAdmissionStatus.Blocked, statuses);
    }
}
