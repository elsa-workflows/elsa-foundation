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
        // FeatureOptions is default-constructable, so IOptions<FeatureOptions> materializes through the standard
        // options pipeline (used by VariableFunctionsPreProcessor) and hosts can override it with the idiomatic
        // services.Configure<FeatureOptions>(...). AddOptions is idempotent and guarantees the options infrastructure
        // is present regardless of host composition order.
        services.AddOptions();

        services
            .AddScoped<IScriptPostProcessor, CopyVariablesToWorkflowContext>()
            .AddScoped<IScriptPreProcessor, WorkflowVariablesContextPreProcessor>()
            .AddScoped<IScriptPreProcessor, WorkflowInputFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, WorkflowFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, VariableFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, ActivityOutputFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();
    }
}
