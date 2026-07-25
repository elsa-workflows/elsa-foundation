using Elsa.Foundation.Identity.OpenIddict.Groundwork;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Persistence.Groundwork;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkRegistrationTests
{
    [Fact]
    public void Groundwork_deployment_source_is_public_and_parameterless()
    {
        var source = new OpenIddictGroundworkStorageManifestSource();

        Assert.Equal("elsa-openiddict", source.FeatureIdentity);
    }

    [Fact]
    public async Task Deployment_source_declares_the_four_openiddict_store_families()
    {
        var declaration = await new OpenIddictGroundworkStorageManifestSource().CreateDeclarationAsync();

        Assert.Equal(4, declaration.Manifest.StorageUnits.Count);
        Assert.All(declaration.ScopeClassifications.Values, value =>
            Assert.Equal(Elsa.Persistence.Groundwork.Composition.GroundworkStorageScopeClassification.ExplicitlyGlobal, value));
        Assert.Contains(declaration.TopologyRequirements, requirement =>
            requirement.Identity == RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity);
    }
}
