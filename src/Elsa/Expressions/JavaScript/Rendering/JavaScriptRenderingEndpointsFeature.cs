using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Elsa.Expressions.JavaScript.Rendering;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ManifestFeatureCategory("API")]
[ShellFeature(
      name: "JavaScriptRenderingEndpoints",
      DisplayName = "JavaScript rendering endpoints",
      Description = "Exposes JavaScript rendering endpoints for design-time declaration document generation."
   )]
public sealed class JavaScriptRenderingEndpointsFeature : FastEndpointsFeatureBase
{
}
