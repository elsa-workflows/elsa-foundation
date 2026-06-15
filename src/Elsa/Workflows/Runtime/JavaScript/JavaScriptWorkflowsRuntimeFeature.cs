using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.PostProcessors;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptWorkflowsRuntime",
    DisplayName = "JavaScript Workflows Runtime",
    Description = "Adds workflow runtime pre-processors and post-processors for JavaScript execution."
)]
public class JavaScriptWorkflowsRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IScriptPostProcessor, CopyVariablesToWorkflowContext>()
            .AddScoped<IScriptPreProcessor, WorkflowVariablesContextPreProcessor>()
            .AddScoped<IScriptPreProcessor, WorkflowInputFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, WorkflowFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, VariableFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, ActivityOutputFunctionsPreProcessor>();
    }
}
