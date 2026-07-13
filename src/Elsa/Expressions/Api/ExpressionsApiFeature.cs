using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Expressions.Api.Capabilities;

namespace Elsa.Expressions.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ExpressionsApi",
    DisplayName = "Expressions API",
    Description = "Canonical management-client endpoints for expression and variable-type descriptors.",
    DependsOn = new object[] { "Expressions", "ApiCapabilities" })]
public sealed class ExpressionsApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddRequestHandlersFrom(GetType().Assembly);
        services.AddApiCapability(ExpressionsApiCapabilities.StaticDeclaration);
    }
}
