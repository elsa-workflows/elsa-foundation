using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Expressions.Api.Capabilities;
using Elsa.Expressions.Api.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Elsa.Expressions.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ExpressionsApi",
    DisplayName = "Expressions API",
    Description = "Canonical management-client endpoints for expression and variable-type descriptors.",
    DependsOn = new object[] { "Expressions", "ApiCapabilities" })]
public sealed class ExpressionsApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddRequestHandlersFrom(GetType().Assembly);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddApiCapability(ExpressionsApiCapabilities.StaticDeclaration);
        services.AddPermissionContributor<ExpressionsPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ExpressionsApi.MapExpressionsApi(endpoints);
}
