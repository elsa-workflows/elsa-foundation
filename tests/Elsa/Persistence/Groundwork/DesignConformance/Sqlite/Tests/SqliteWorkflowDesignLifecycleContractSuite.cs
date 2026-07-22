using Elsa.Persistence.Groundwork.DesignConformance.Tests;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// Executes the converted (T070) workflow-design lifecycle contract — metadata update, aggregate
/// isolation, promotion-gate rejection, and the active-definition permanent-delete guard — on the
/// composed SQLite Groundwork target.
/// </summary>
public sealed class SqliteWorkflowDesignLifecycleContractSuite : WorkflowDesignLifecycleContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);
}
