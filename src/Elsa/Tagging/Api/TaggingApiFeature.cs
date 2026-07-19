using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tagging.Api.Capabilities;
using Elsa.Tagging.Api.Services;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;

namespace Elsa.Tagging.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Tagging")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "TaggingApi",
    DisplayName = "Tagging API",
    Description = "Provides tenant-scoped marker tag catalog endpoints for workflow authoring.",
    DependsOn = new object[] { "ApiCapabilities" })]
public class TaggingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddTagging();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.RemoveAll<ITagDefinitionAuditContext>();
        services.AddScoped<ITagDefinitionAuditContext, HttpTagDefinitionAuditContext>();
        services.AddApiCapability(TaggingApiCapabilities.StaticDeclaration);
    }
}
