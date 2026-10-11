using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.ExtensionBuilder;

/// <summary>
/// The optional Extension Builder Studio module (#535). It contributes the Extension Builder page (navigation + route)
/// and relays the page's calls to the backend Elsa host's Extension Builder surface through the Studio management
/// bridge (#256, ADR 0037), nested under the host's bridge route group. A Studio that does not enable this feature
/// has no Extension Builder navigation item, route, or bridge endpoint.
///
/// <para>The feature relies on the host's bridge infrastructure: the <c>StudioBackendManagementOptions</c> singleton
/// (backend base URL + server-side management key) and the Studio bridge authentication scheme with its permission
/// handler. Without the handler every endpoint fails closed (403); without the options the relay clients cannot be
/// constructed.</para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Extension Builder")]
[StudioModule("extension-builder", "Extension Builder", "navigation", "routes", "http")]
[ShellFeature(
    name: "ExtensionBuilderStudio",
    DisplayName = "Extension Builder Studio module",
    Description = "Contributes the Extension Builder page and relays its operations to the backend Extension Builder through the Studio management bridge."
)]
public sealed class ExtensionBuilderStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddStudioExtensionBuilderBridge();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapStudioExtensionBuilderBridge();
    }
}
