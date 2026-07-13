using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class SecretsStudioModuleTests
{
    [Fact]
    public async Task GetModules_ReturnsSecretsManifestAssetsAndCapabilities()
    {
        using var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Secrets");
        Assert.Equal("Secrets", module.DisplayName);
        Assert.Equal("SecretsStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.Secrets/studio/modules/secrets/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Secrets/studio/modules/secrets/module.css", StringComparison.Ordinal));
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("expression-editors", module.Capabilities);
        Assert.DoesNotContain("property-editors", module.Capabilities);
        Assert.Contains(response.Diagnostics, x => x.ModuleId == "Elsa.Studio.Secrets" && x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddSecretsStudio();
        return services.BuildServiceProvider();
    }
}
