using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptActivities",
    DisplayName = "JavaScript activities",
    Description = "Provides runtime support for JavaScript-backed workflow activities.",
    DependsOn = new object[] { "JavaScriptJintEngine" }
)]
public class JavaScriptActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // JavaScriptJintEngine supplies the isolated evaluator used by the endpoint. Workflow
        // authoring uses the canonical typed activity from Elsa.Activities.Scripting instead of
        // maintaining a second activity and a fake runtime execution context in this package.
    }
}
