using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>Executes the T064 additive schema-evolution and safe-apply-recovery contract on SQL Server.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerUnifiedSchemaEvolutionContractSuite(SqlServerDesignProviderFixture container)
    : UnifiedSchemaEvolutionContractSuite
{
    protected override async Task<IDesignSchemaEvolutionFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignSchemaEvolutionFixture.CreateAsync(container, cancellationToken);
}
