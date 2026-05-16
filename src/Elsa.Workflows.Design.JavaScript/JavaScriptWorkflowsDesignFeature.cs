using CShells.Features;
using Elsa.Workflows.Design.JavaScript.Providers;
using Elsa.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.JavaScript
{
    [ShellFeature(
        name: "JavaScriptWorkflows",
        DisplayName = "JavaScript Workflows"
    )]
    public class JavaScriptWorkflowsDesignFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services                
                .AddAsInterfaces<ActivityOutputFunctionDeclarationsProvider>()
                .AddAsInterfaces<CommonFunctionDeclarationsProvider>()
                .AddAsInterfaces<WorkflowInputFunctionDeclarationsProvider>()
                .AddAsInterfaces<WorkflowVariableFunctionDeclarationsProvider>()
                .AddAsInterfaces<WorkflowVariablesDeclarationProvider>()
                ;
        }
    }
}
