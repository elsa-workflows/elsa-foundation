using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityPostgreSqlProviderTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task PostgreSql_physical_identity_scenario_survives_restart_and_rejects_duplicate_race()
    {
        await using var driver = new PostgreSqlGroundworkProviderDriver();
        var acceptance = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync(CancellationToken.None);

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(acceptance.CompletedObjectiveIds);
    }
}
