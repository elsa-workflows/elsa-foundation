using CShells.Features;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Connections;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class ConnectionsStudioModuleTests
{
    [Fact]
    public async Task GetModules_ReturnsConnectionsManifestAssetsAndCapabilities()
    {
        using var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Connections");
        Assert.Equal("Connections", module.DisplayName);
        Assert.Equal("ConnectionsStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.Connections/studio/modules/connections/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Connections/studio/modules/connections/module.css", StringComparison.Ordinal));
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("expression-editors", module.Capabilities);
        Assert.DoesNotContain("property-editors", module.Capabilities);
        Assert.Contains(response.Diagnostics, x => x.ModuleId == "Elsa.Studio.Connections" && x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(typeof(ConnectionsStudioFeature)));
        return services.BuildServiceProvider();
    }

}
