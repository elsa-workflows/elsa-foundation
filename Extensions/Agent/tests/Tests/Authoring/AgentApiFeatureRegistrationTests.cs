using Elsa.Agent.Api;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Authoring;

public sealed class AgentApiFeatureRegistrationTests
{
    [Fact]
    public void Authoring_api_declares_its_capability_with_source_and_model_links_and_its_fault_renderer()
    {
        var services = new ServiceCollection();
        new AgentApiFeature().ConfigureServices(services);

        var capability = Assert.Single(services.Where(x => x.ImplementationInstance is ApiCapabilityDeclaration).Select(x => (ApiCapabilityDeclaration)x.ImplementationInstance!),
            x => x.CapabilityId == "elsa.api.agent");
        Assert.Equal(["agent-model-capabilities", "agent-model-providers"], capability.Links.Select(x => x.Rel).Order(StringComparer.Ordinal));
        var definitions = Assert.Single(services.Where(x => x.ImplementationInstance is ApiCapabilityDeclaration).Select(x => (ApiCapabilityDeclaration)x.ImplementationInstance!),
            x => x.CapabilityId == "elsa.api.agent-definitions");
        Assert.Equal(new[] { "agent-source-captures", "agent-source-definitions", "agent-source-discard", "agent-source-draft",
            "agent-source-draft-revision", "agent-source-draft-revisions", "agent-source-forks", "agent-source-revision", "agent-source-revisions" },
            definitions.Links.Select(x => x.Rel).Order(StringComparer.Ordinal));
        Assert.Equal("agent/definitions", Assert.Single(definitions.Links, x => x.Rel == "agent-source-definitions").Href);
        Assert.Equal("agent/definitions/revision", Assert.Single(definitions.Links, x => x.Rel == "agent-source-revision").Href);
        Assert.Equal("agent/definitions/draft", Assert.Single(definitions.Links, x => x.Rel == "agent-source-draft").Href);
        Assert.All(definitions.Links, link =>
        {
            Assert.False(link.Templated);
            Assert.DoesNotContain('?', link.Href);
            Assert.DoesNotContain('{', link.Href);
        });
        Assert.Single(services, x => x.ServiceType == typeof(MinimalEndpoints.IEndpointFaultRenderer) && x.IsKeyedService && Equals(x.ServiceKey, AgentApi.OwnerId)
            && x.KeyedImplementationType == typeof(AgentApiFaultRenderer));
    }
}
