using Elsa.Persistence.Groundwork.Diagnostics;
using Elsa.Persistence.Groundwork.Options;
using Elsa.Persistence.Groundwork.Secrets;
using Elsa.Persistence.Groundwork.Tasks;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkPersistenceFeatureTests
{
    [Fact]
    public void FeatureRegistersManifestStartupTaskAndDiagnostics()
    {
        var services = new ServiceCollection();
        var manifest = SecretsGroundworkManifestFactory.Create();
        var feature = new GroundworkPersistenceFeature { MaterializeOnStartup = false };
        feature.Manifests.Add(manifest);

        feature.ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupTask) &&
            descriptor.ImplementationType == typeof(MaterializeGroundworkStartupTask));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GroundworkPersistenceOptions>>().Value;
        var diagnostics = provider.GetRequiredService<GroundworkPersistenceDiagnostics>();
        var snapshot = diagnostics.GetSnapshot();

        Assert.False(options.MaterializeOnStartup);
        Assert.Contains(options.Manifests, registered => registered.Identity == manifest.Identity);
        Assert.Contains(snapshot.Manifests, registered => registered.Identity == manifest.Identity.Value);
    }
}
