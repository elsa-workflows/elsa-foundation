using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class StudioApiVersionBindingTests
{
    private static readonly StudioModuleManifest RequiresHostV2 = new(
        "Test.Module.NeedsHostV2",
        "Needs host v2",
        "1.0.0",
        "/module.js",
        [],
        RequiredHostVersion: "^2.0.0",
        RequiredSdkVersion: "*",
        Capabilities: ["routes"]);

    [Fact]
    public async Task HostVersion_DefaultsToAssemblyVersion_LeavingModuleIncompatible()
    {
        var provider = BuildProvider(configuration: null);

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);

        // Assembly version is 1.0.0, so a module requiring host ^2.0.0 is incompatible.
        Assert.Equal(StudioApiOptions.ResolveAssemblyVersion(), response.HostVersion);
        var module = Assert.Single(response.Modules, x => x.Id == RequiresHostV2.Id);
        Assert.Equal("incompatible", module.Compatibility);
    }

    [Fact]
    public async Task HostVersion_BoundFromConfiguration_FlipsModuleToCompatible()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Studio:Api:HostVersion"] = "2.3.1"
            })
            .Build();

        var provider = BuildProvider(configuration);

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);

        // The configured host version flows through to the compatibility gate and flips the verdict.
        Assert.Equal("2.3.1", response.HostVersion);
        var module = Assert.Single(response.Modules, x => x.Id == RequiresHostV2.Id);
        Assert.Equal("compatible", module.Compatibility);
    }

    private static ServiceProvider BuildProvider(IConfiguration? configuration)
    {
        var services = new ServiceCollection();
        if (configuration is not null)
            services.AddSingleton(configuration);

        services.AddElsaStudioApi();
        services.AddSingleton<IStudioEventHandler<OnStudioModuleManifestsCollecting>>(new StubManifestContributor(RequiresHostV2));
        return services.BuildServiceProvider();
    }

    private sealed class StubManifestContributor(StudioModuleManifest manifest)
        : IStudioEventHandler<OnStudioModuleManifestsCollecting>
    {
        public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
        {
            @event.Manifests.Add(manifest);
            return Task.CompletedTask;
        }
    }
}
