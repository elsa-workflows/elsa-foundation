using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
namespace Elsa.Studio.Attention;
[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)] [ManifestFeatureCategory("Studio")] [ManifestFeatureCategory("Attention")]
[ShellFeature(name: "AttentionStudio", DisplayName = "Attention", Description = "Presents attention items in Studio.")]
public sealed class AttentionStudioFeature : IShellFeature { public void ConfigureServices(IServiceCollection services) => services.AddAttentionStudio(); }
