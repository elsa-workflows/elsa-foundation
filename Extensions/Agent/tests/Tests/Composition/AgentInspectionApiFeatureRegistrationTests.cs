using Elsa.Agent.Inspection.Api;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Composition;

public sealed class AgentInspectionApiFeatureRegistrationTests
{
    [Fact]
    public void Inspection_api_declares_its_capability_with_both_evidence_links_and_its_fault_renderer()
    {
        var services = new ServiceCollection();
        new AgentInspectionApiFeature().ConfigureServices(services);

        var capability = Assert.Single(services.Where(x => x.ImplementationInstance is ApiCapabilityDeclaration).Select(x => (ApiCapabilityDeclaration)x.ImplementationInstance!),
            x => x.CapabilityId == "elsa.api.agent-inspection");
        Assert.Equal([("agent-invocation-evidence", AgentInspectionApi.InvocationEvidenceRoute), ("agent-test-run-evidence", AgentInspectionApi.TestRunEvidenceRoute)],
            capability.Links.Select(x => (x.Rel, x.Href)).Order());
        Assert.Single(services, x => x.ServiceType == typeof(MinimalEndpoints.IEndpointFaultRenderer) && x.IsKeyedService && Equals(x.ServiceKey, AgentInspectionApi.OwnerId)
            && x.KeyedImplementationType == typeof(AgentInspectionFaultRenderer));
    }
}
