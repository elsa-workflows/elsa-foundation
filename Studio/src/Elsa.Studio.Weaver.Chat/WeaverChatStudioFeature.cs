using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Chat;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("AI")]
[ShellFeature(
    name: "WeaverChatStudio",
    DisplayName = "Weaver chat Studio module",
    Description = "Contributes optional Weaver chat, sessions, prompt queue, and contextual AI surfaces to Elsa Studio."
)]
public sealed class WeaverChatStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWeaverChatStudio();
    }
}
