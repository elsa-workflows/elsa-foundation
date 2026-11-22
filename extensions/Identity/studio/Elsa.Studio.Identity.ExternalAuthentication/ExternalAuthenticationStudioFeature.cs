using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Identity.ExternalAuthentication;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Identity")]
[StudioModule("external-authentication", "External sign-in", "navigation", "routes", "http", "identity")]
[ShellFeature(name: "ExternalAuthenticationStudio", DisplayName = "External sign-in Studio module",
    Description = "Manage external sign-in connections, test configuration and preview provisioning.")]
public class ExternalAuthenticationStudioFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) { }
}
