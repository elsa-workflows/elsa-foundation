using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Workflows.Processors;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.JavaScript.Processors;
using Elsa.Workflows.Runtime.JavaScript.Providers;
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
                .AddScoped<IJavaScriptEvaluationPostProcessor, CopyVariablesFromEnginePostProcessor>()
                .AddScoped<IJavaScriptEvaluationPreProcessor, CopyVariablesIntoEnginePreProcessor>()

                .AddAsInterfaces<WorkflowInputFunctionsProvider>()
                .AddAsInterfaces<ActivityOutputFunctionsProvider>()
                .AddAsInterfaces<CommonFunctionsProvider>();
        }
    }
}
