using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// T051: explicit provider-level close/reopen and schema-drift hooks for the SQLite leaf. The
/// contract suites already restart between phases; these hooks pin the provider promises directly:
/// closing and reopening the same database preserves admission and durable design state, and
/// physical schema drift after application is a blocking readiness failure on both the deployment
/// (CLI validate) and runtime (admission on reopen) paths — never an empty store, never a fallback.
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
    public async Task Physical_schema_drift_after_application_is_a_blocking_readiness_failure()
    {
        await using var fixture = await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry);
        await fixture.ValidateReadinessAsync();

        // Drift: drop one applied Groundwork design index directly on the database file.
        string droppedIndex;
        await using (var connection = new SqliteConnection(fixture.SqliteConnectionString))
        {
            await connection.OpenAsync();
            await using (var find = connection.CreateCommand())
            {
                find.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'index' " +
                    "AND sql IS NOT NULL AND name LIKE '%definition%' ORDER BY name LIMIT 1";
                droppedIndex = (string?)await find.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("No applied design index was found to drift.");
            }

            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP INDEX \"{droppedIndex}\"";
            await drop.ExecuteNonQueryAsync();
        }

        // Deployment path: live validation reports the incompatibility instead of ready.
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.ValidateReadinessAsync());

        // Runtime path: reopening the composed host must fail admission — no auto-apply, no
        // silent fallback to an unvalidated store.
        var admission = await Assert.ThrowsAnyAsync<Exception>(() => fixture.RestartAsync());
        Assert.Contains(
            "admission",
            admission.GetType().Name + " " + admission.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
