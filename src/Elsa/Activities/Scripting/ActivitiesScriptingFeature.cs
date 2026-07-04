using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Scripting;

/// <summary>
/// Scripting activities: <see cref="Activities.RunJavaScript"/>, which executes an authored JavaScript script
/// through the shared Jint evaluator (W16). The activity type is resolved by the runtime's
/// <c>ClrActivityConstructor</c> — no per-type DI registration is required — so this feature adds no services of
/// its own; it declares the dependency on the JavaScript Jint engine feature (which registers
/// <c>IJavaScriptEvaluator</c>) so composing this feature yields a working <see cref="Activities.RunJavaScript"/>.
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
        // RunJavaScript is a CLR activity constructed by the runtime; the JavaScript evaluator it depends on is
        // registered by the JavaScriptJintEngine feature declared above. No additional services are required.
    }
}
