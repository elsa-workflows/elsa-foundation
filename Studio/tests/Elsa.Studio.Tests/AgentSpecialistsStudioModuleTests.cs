using CShells.Features;
using Elsa.Studio.Agent.Specialists;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class AgentSpecialistsStudioModuleTests
{
    [Fact]
    public async Task GetModules_ReturnsTheAgentReviewModuleWithRoutesAndHttpOnly()
    {
        using var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Agent.Specialists");
        Assert.Equal("Agents", module.DisplayName);
        Assert.Equal("AgentSpecialistsStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.Agent.Specialists/studio/modules/agent-specialists/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains("routes", module.Capabilities);
        Assert.DoesNotContain("navigation", module.Capabilities);
        Assert.Contains(response.Diagnostics, x => x.ModuleId == "Elsa.Studio.Agent.Specialists" && x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(typeof(AgentSpecialistsStudioFeature)));
        return services.BuildServiceProvider();
    }
}
