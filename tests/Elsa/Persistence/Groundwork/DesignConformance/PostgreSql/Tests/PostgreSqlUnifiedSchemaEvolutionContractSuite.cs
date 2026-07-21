using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>Executes the T064 additive schema-evolution and safe-apply-recovery contract on PostgreSQL.</summary>
[Collection(PostgreSqlDesignProviderCollection.Name)]
public sealed class PostgreSqlUnifiedSchemaEvolutionContractSuite(PostgreSqlDesignProviderFixture container)
    : UnifiedSchemaEvolutionContractSuite
{
    protected override async Task<IDesignSchemaEvolutionFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await PostgreSqlDesignSchemaEvolutionFixture.CreateAsync(container, cancellationToken);
}
