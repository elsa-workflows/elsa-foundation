using Elsa.Agent.Specialists.Inspection.Api;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

public sealed class SpecialistsInspectionApiFeatureRegistrationTests
{
    [Fact]
    public void Inspection_api_declares_its_capability_with_both_evidence_links_and_its_fault_renderer()
    {
        var services = new ServiceCollection();
        new SpecialistsInspectionApiFeature().ConfigureServices(services);

        var capability = Assert.Single(services.Where(x => x.ImplementationInstance is ApiCapabilityDeclaration).Select(x => (ApiCapabilityDeclaration)x.ImplementationInstance!),
            x => x.CapabilityId == "elsa.api.agent-specialist-inspection");
        Assert.Equal([("agent-specialist-invocation-evidence", SpecialistInspectionApi.InvocationEvidenceRoute), ("agent-specialist-test-run-evidence", SpecialistInspectionApi.TestRunEvidenceRoute)],
            capability.Links.Select(x => (x.Rel, x.Href)).Order());
        Assert.Single(services, x => x.ServiceType == typeof(NativeEndpoints.IEndpointFaultRenderer) && x.IsKeyedService && Equals(x.ServiceKey, SpecialistInspectionApi.OwnerId)
            && x.KeyedImplementationType == typeof(SpecialistInspectionFaultRenderer));
    }
}
