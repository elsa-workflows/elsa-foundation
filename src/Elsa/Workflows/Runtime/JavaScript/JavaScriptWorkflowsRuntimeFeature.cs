using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Elsa.Workflows.Runtime.JavaScript.PostProcessors;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        // FeatureOptions is a positional record with no parameterless constructor, so the default
        // IOptions<FeatureOptions> creation path cannot materialize it. Register a default instance so
        // VariableFunctionsPreProcessor resolves; hosts can replace it with their own configured options.
        services.TryAddSingleton<IOptions<FeatureOptions>>(Microsoft.Extensions.Options.Options.Create(new FeatureOptions(DisableVariableCopying: false)));

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
