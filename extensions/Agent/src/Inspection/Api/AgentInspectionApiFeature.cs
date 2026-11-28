using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Inspection.Api;

/// <summary>Programmatically registers evidence reads for the classic agent runtime.</summary>
public class AgentInspectionApiFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.TryAddScoped<AgentEvidenceAccess>();
        services.TryAddKeyedSingleton<MinimalEndpoints.IEndpointFaultRenderer, AgentInspectionFaultRenderer>(AgentInspectionApi.OwnerId);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-inspection", 1,
        [
            new("agent-test-run-evidence", AgentInspectionApi.TestRunEvidenceRoute, true),
            new("agent-invocation-evidence", AgentInspectionApi.InvocationEvidenceRoute, true)
        ], "AgentInspectionApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => AgentInspectionApi.MapAgentInspectionApi(endpoints);
}
