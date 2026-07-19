using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ExpressionEditors.Liquid;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("expression-editors/liquid", "Liquid expression editor", "1.0.0", "expression-editors", "liquid")]
[ShellFeature(
    name: "LiquidExpressionEditorStudio",
    DisplayName = "Liquid expression editor Studio module",
    Description = "Contributes Liquid expression editor surfaces to Elsa Studio workflow properties."
)]
public sealed class LiquidExpressionEditorStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
