using Elsa.Persistence.Groundwork.DesignConformance.Tests;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>Executes the T064 additive schema-evolution and safe-apply-recovery contract on SQLite.</summary>
public sealed class SqliteUnifiedSchemaEvolutionContractSuite : UnifiedSchemaEvolutionContractSuite
{
    protected override async Task<IDesignSchemaEvolutionFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignSchemaEvolutionFixture.CreateAsync(cancellationToken);
}
