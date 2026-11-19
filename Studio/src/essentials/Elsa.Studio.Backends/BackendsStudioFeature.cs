using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Api.Features;
using Elsa.Studio.Core.Attributes;
using Elsa.Studio.Core.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Backends;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Backends")]
[StudioModule("backends", "Backends", "navigation", "routes", "connection-selection", "shell-actions", "host-initialization")]
[ShellFeature("BackendsStudio", DisplayName = "Backend connections", Description = "Manages optional Studio backend profiles and selection.", DependsOn = new object[] { typeof(StudioApiFeature) })]
public class BackendsStudioFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        StudioBackendProfiles.Add(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStudioRuntimeConfigurationSource, BackendsRuntimeConfigurationSource>());
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => StudioBackendProfiles.Map(endpoints);
}
