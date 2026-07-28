using System.Globalization;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentitySqlServerProviderTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_physical_identity_scenario_survives_restart_and_rejects_duplicate_race()
    {
        await using var driver = new SqlServerGroundworkProviderDriver();
        var acceptance = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync(CancellationToken.None);

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(acceptance.CompletedObjectiveIds);
        var budget = await driver.CaptureIdentityCompoundKeyBudgetAsync(CancellationToken.None);
        Assert.Equal("compound-key-budget", budget.Kind);
        Assert.InRange(
            ParseDeclaredKeyBytes(budget.Content),
            1,
            IdentityStorageManifest.SqlServerMaxNonclusteredIndexKeyBytes);
    }

    private static int ParseDeclaredKeyBytes(string content)
    {
        const string Marker = "declared-key-bytes=";
        var markerStart = content.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, content);
        var valueStart = markerStart + Marker.Length;
        var valueEnd = content.IndexOfAny([';', '\r', '\n'], valueStart);
        var value = valueEnd >= 0 ? content[valueStart..valueEnd] : content[valueStart..];
        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}
