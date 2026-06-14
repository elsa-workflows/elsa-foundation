using Elsa.Features.Abstractions;
using Elsa.Features.Abstractions.Extensions;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class CapabilityRegistrationTests
{
    [Fact]
    public async Task Provider_ReturnsCapabilitiesOrderedById()
    {
        using var provider = CreateProvider(services =>
        {
            services.AddElsaCapability("z.capability", "Z", "ZFeature");
            services.AddElsaCapability("a.capability", "A", "AFeature");
        });

        var capabilities = await provider.GetRequiredService<IElsaCapabilityProvider>().GetCapabilitiesAsync();

        Assert.Collection(
            capabilities,
            capability => Assert.Equal("a.capability", capability.Id),
            capability => Assert.Equal("z.capability", capability.Id));
    }

    [Fact]
    public async Task Provider_DeduplicatesCapabilityIdsDeterministically()
    {
        using var provider = CreateProvider(services =>
        {
            services.AddElsaCapability("elsa.test", "Later source", "ZFeature");
            services.AddElsaCapability("elsa.test", "Earlier source", "AFeature");
        });

        var capability = Assert.Single(await provider.GetRequiredService<IElsaCapabilityProvider>().GetCapabilitiesAsync());

        Assert.Equal("AFeature", capability.SourceFeature);
        Assert.Equal("Earlier source", capability.DisplayName);
    }

    [Fact]
    public async Task EnabledFeatures_ExposeCapabilitiesThroughProvider()
    {
        using var provider = CreateProvider(services =>
        {
            new WorkflowsPublishingApiFeature().ConfigureServices(services);
            new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        });

        var capabilities = await provider.GetRequiredService<IElsaCapabilityProvider>().GetCapabilitiesAsync();

        Assert.Contains(capabilities, x => x.Id == ElsaCapabilities.WorkflowsPublishing);
        Assert.Contains(capabilities, x => x.Id == ElsaCapabilities.WorkflowsRuntime);
    }

    private static ServiceProvider CreateProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }
}
