using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentitySqliteProviderTests
{
    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task File_backed_sqlite_physical_identity_scenario_survives_reopen_and_process_restart()
    {
        await using var driver = new SqliteGroundworkProviderDriver();
        var acceptance = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync(CancellationToken.None);

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(acceptance.CompletedObjectiveIds);
    }
}
