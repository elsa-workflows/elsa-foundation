using CShells.Features;
using Elsa.Studio.Agent;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class AgentStudioModuleTests
{
    [Fact]
    public async Task GetModules_ReturnsTheAgentsModuleWithNavigationRoutesAndActivityEditors()
    {
        using var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Agent");
        Assert.Equal("Agents", module.DisplayName);
        Assert.Equal("AgentStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.Agent/studio/modules/agent/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("navigation", module.Capabilities);
        Assert.Contains("activity-editors", module.Capabilities);
        Assert.Contains(response.Diagnostics, x => x.ModuleId == "Elsa.Studio.Agent" && x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(typeof(AgentStudioFeature)));
        return services.BuildServiceProvider();
    }
}
