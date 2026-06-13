using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Elsa.Workflows.Runtime.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "JavaScriptEndpoints",
    DisplayName = "JavaScript FastEndpoints",
    Description = "Exposes runtime API endpoints for JavaScript workflow activities."
)]
public sealed class JavaScriptActivitiesEndpointsFeature : FastEndpointsFeatureBase
{
}
