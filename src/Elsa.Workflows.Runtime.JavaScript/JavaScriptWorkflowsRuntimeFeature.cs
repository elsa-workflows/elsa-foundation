using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.PostProcessors;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript
{
    [ShellFeature(
        name: "JavaScriptWorkflows",
        DisplayName = "JavaScript Workflows"
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
}
