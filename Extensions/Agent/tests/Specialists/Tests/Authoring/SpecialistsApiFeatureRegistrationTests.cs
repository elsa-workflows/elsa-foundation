using Elsa.Agent.Specialists.Api;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Authoring;

public sealed class SpecialistsApiFeatureRegistrationTests
{
    [Fact]
    public void Authoring_api_declares_its_capability_with_source_and_model_links_and_its_fault_renderer()
    {
        var services = new ServiceCollection();
        new SpecialistsApiFeature().ConfigureServices(services);

        var capability = Assert.Single(services.Where(x => x.ImplementationInstance is ApiCapabilityDeclaration).Select(x => (ApiCapabilityDeclaration)x.ImplementationInstance!),
            x => x.CapabilityId == "elsa.api.agent-specialists");
        Assert.Equal(["agent-source-definitions", "agent-specialist-model-capabilities", "agent-specialist-model-providers"], capability.Links.Select(x => x.Rel).Order(StringComparer.Ordinal));
        Assert.Single(services, x => x.ServiceType == typeof(MinimalEndpoints.IEndpointFaultRenderer) && x.IsKeyedService && Equals(x.ServiceKey, SpecialistsApi.OwnerId)
            && x.KeyedImplementationType == typeof(SpecialistsApiFaultRenderer));
    }
}
