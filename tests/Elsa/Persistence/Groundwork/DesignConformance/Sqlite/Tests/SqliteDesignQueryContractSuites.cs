using Elsa.Persistence.Groundwork.DesignConformance.Tests;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>Executes the T037 workflow query-shape parity contract on the composed SQLite target.</summary>
public sealed class SqliteWorkflowDesignQueryContractSuite : WorkflowDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);
}

/// <summary>Executes the T038 activity query-shape parity contract on the composed SQLite target.</summary>
public sealed class SqliteActivityDesignQueryContractSuite : ActivityDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);
}
