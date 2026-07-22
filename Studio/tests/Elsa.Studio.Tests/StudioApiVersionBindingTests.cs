using System.Reflection;
using CShells.Features;
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

    [Theory]
    // No configured host version defaults to the host assembly version (1.0.0), leaving a module requiring ^2.0.0 incompatible.
    [InlineData(null, null, "incompatible")]
    // A configured host version flows through to the compatibility gate and flips the verdict.
    [InlineData("2.3.1", "2.3.1", "compatible")]
    public async Task HostVersion_BindsFromConfiguration_AndGatesModuleCompatibility(
        string? configuredHostVersion, string? expectedHostVersion, string expectedCompatibility)
    {
        var configuration = configuredHostVersion is null
            ? null
            : new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:Api:HostVersion"] = configuredHostVersion })
                .Build();

        var provider = BuildProvider(configuration);

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);

        Assert.Equal(expectedHostVersion ?? StudioApiOptions.ResolveAssemblyVersion(), response.HostVersion);
        var module = Assert.Single(response.Modules, x => x.Id == RequiresHostV2.Id);
        Assert.Equal(expectedCompatibility, module.Compatibility);
    }

    private static ServiceProvider BuildProvider(IConfiguration? configuration)
    {
        var services = new ServiceCollection();
        if (configuration is not null)
            services.AddSingleton(configuration);

        services.AddElsaStudioApi();
        services.AddSingleton<IRuntimeFeatureCatalog>(new EmptyRuntimeFeatureCatalog());
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

    private sealed class EmptyRuntimeFeatureCatalog : IRuntimeFeatureCatalog
    {
        private static readonly RuntimeFeatureCatalogSnapshot s_empty = new(
            0,
            Array.Empty<Assembly>(),
            Array.Empty<ShellFeatureDescriptor>(),
            new Dictionary<string, ShellFeatureDescriptor>(),
            DateTimeOffset.UtcNow);

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(s_empty);

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(s_empty);
    }
}
