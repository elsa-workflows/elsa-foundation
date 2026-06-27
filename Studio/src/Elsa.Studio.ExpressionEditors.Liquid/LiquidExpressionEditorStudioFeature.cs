using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ExpressionEditors.Liquid;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "LiquidExpressionEditorStudio",
    DisplayName = "Liquid expression editor Studio module",
    Description = "Contributes Liquid expression editor surfaces to Elsa Studio workflow properties."
)]
public sealed class LiquidExpressionEditorStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLiquidExpressionEditorStudio();
    }
}
