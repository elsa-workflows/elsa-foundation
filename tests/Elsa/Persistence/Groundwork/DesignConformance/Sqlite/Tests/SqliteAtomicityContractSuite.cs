using Elsa.Persistence.Groundwork.DesignConformance.Tests;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// Makes the target-profile atomicity contract executable as individually reported SQLite tests.
/// The baseline catalog consumes the same fixture, while this suite isolates a failed fault window.
/// </summary>
public sealed class SqliteAtomicityContractSuite : DesignAtomicityContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);
}
