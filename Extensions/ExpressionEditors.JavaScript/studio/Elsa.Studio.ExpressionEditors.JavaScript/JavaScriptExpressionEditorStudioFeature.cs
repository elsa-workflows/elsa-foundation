using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ExpressionEditors.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("expression-editors/javascript", "JavaScript expression editor", "expression-editors", "javascript")]
[ShellFeature(
    name: "JavaScriptExpressionEditorStudio",
    DisplayName = "JavaScript expression editor Studio module",
    Description = "Contributes JavaScript expression editor surfaces to Elsa Studio workflow properties."
)]
public sealed class JavaScriptExpressionEditorStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
