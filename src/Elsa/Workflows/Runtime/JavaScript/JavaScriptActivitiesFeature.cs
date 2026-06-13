using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.TestClasses;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptActivities",
    DisplayName = "JavaScript activities",
    Description = "Provides runtime support for JavaScript-backed workflow activities."
)]
public class JavaScriptActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IActivityCompletionHandler, ActivityCompletionHandler>();
    }
}
