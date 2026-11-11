using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Review.Api;

/// <summary>The permission-gated read and decision endpoints for one exact human review wait.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "AgentReviewApi", DisplayName = "Agents: Human Review API",
    Description = "Read exact content awaiting human review and approve or deny it with the current review permission.",
    DependsOn = new object[] { "AgentReview", "ApiCapabilities" })]
public class AgentReviewApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.TryAddScoped<ReviewRequestCaller>();
        services.TryAddKeyedSingleton<MinimalEndpoints.IEndpointFaultRenderer, HumanReviewFaultRenderer>(HumanReviewApi.OwnerId);
        services.AddPermissionContributor<HumanReviewPermissionContributor>();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-reviews", 1,
        [
            new("agent-review", HumanReviewApi.Route, true),
            new("agent-review-decision", HumanReviewApi.Route + "/decision", true)
        ], "AgentReviewApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => HumanReviewApi.MapHumanReviewApi(endpoints);
}
