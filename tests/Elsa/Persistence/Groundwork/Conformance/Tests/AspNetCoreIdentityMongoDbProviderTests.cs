using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityMongoDbProviderTests
{
    [Fact]
    [Trait("Category", "MongoDb")]
    public async Task MongoDb_replica_set_physical_identity_scenario_survives_restart_and_rejects_standalone()
    {
        await using var driver = new MongoDbGroundworkProviderDriver();
        var acceptance = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync(CancellationToken.None);

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(acceptance.CompletedObjectiveIds);
        var topologyRejection = await driver.CaptureTopologyRejectionAsync(CancellationToken.None);
        Assert.Equal("topology-rejection", topologyRejection.Kind);
        Assert.Contains("observed-topology=standalone", topologyRejection.Content, StringComparison.Ordinal);
        Assert.Contains("outcome=rejected", topologyRejection.Content, StringComparison.Ordinal);
    }
}
