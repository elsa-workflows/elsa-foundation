using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityApplications",
    DisplayName = "Foundation Identity Applications",
    Description = "Manages applications and their API keys under /_elsa/identity/applications and authenticates Authorization: ApiKey requests as the application. Needs an application and credential store, such as IdentityIamEntityFrameworkCore."
)]
public class FoundationIdentityApplicationsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddFoundationIdentityApplications();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        FoundationIdentityApplicationsApi.MapFoundationIdentityApplicationsApi(endpoints);
}
