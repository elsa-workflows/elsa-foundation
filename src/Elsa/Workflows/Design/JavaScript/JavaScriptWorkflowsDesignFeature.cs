using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Workflows.Design.JavaScript.Contributors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptWorkflowsDesign",
    DisplayName = "JavaScript Workflows Design",
    Description = "Contributes workflow design declarations for JavaScript expression authoring."
)]
public class JavaScriptWorkflowsDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IJavaScriptDeclarationContributor, ActivityOutputFunctionDeclarationContributor>()
            .AddScoped<IJavaScriptDeclarationContributor, WorkflowFunctionDeclarationContributor>()
            .AddScoped<IJavaScriptDeclarationContributor, WorkflowInputFunctionDeclarationContributor>()
            .AddScoped<IJavaScriptDeclarationContributor, WorkflowVariableFunctionDeclarationContributor>()
            .AddScoped<IJavaScriptDeclarationContributor, WorkflowVariablesDeclarationContributor>()
            .AddScoped<IJavaScriptDeclarationContributor, OutcomeFunctionDeclarationContributor>()
            ;
    }
}
