using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Scripting;

/// <summary>
/// Scripting activities: <see cref="Activities.RunJavaScript"/>, which executes an authored JavaScript script
/// through the isolated Jint script evaluator. The activity is transiently activated through DI, which
/// constructor-injects the evaluator before Runtime hydrates its plain input properties. This feature declares
/// the JavaScript Jint dependency and otherwise adds no service-locator or workflow-value surface.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "ActivitiesScripting",
    DisplayName = "Activities Scripting",
    Description = "Scripting activities: RunJavaScript executes an authored JavaScript script through the shared Jint evaluator.",
    DependsOn = new object[] { "JavaScriptJintEngine" }
)]
public sealed class ActivitiesScriptingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // The JavaScriptJintEngine feature supplies IJavaScriptScriptEvaluator for constructor injection.
    }
}
