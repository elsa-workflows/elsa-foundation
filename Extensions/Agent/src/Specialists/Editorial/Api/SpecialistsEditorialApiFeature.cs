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

namespace Elsa.Agent.Specialists.Editorial.Api;

/// <summary>The permission-gated read and decision endpoints for one exact editorial review wait.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "SpecialistsEditorialApi", DisplayName = "Specialists: Editorial Review API",
    Description = "Read an exact editorial review and approve or deny it with the current review permission.",
    DependsOn = new object[] { "SpecialistsEditorial", "ApiCapabilities" })]
public class SpecialistsEditorialApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.TryAddScoped<EditorialRequestCaller>();
        services.TryAddKeyedSingleton<NativeEndpoints.IEndpointFaultRenderer, EditorialReviewFaultRenderer>(EditorialReviewApi.OwnerId);
        services.AddPermissionContributor<EditorialReviewPermissionContributor>();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.editorial-reviews", 1,
        [
            new("editorial-review", EditorialReviewApi.Route, true),
            new("editorial-review-decision", EditorialReviewApi.Route + "/decision", true)
        ], "SpecialistsEditorialApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => EditorialReviewApi.MapEditorialReviewApi(endpoints);
}
