using CShells.Features;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Abstractions;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAbstractions",
    DisplayName = "Foundation Identity Abstractions",
    Description = "Registers provider-agnostic authentication, IAM, authorization, ownership, and security guard contracts."
)]
public class FoundationIdentityAbstractionsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions();
    }
}
