using CShells.Features;
using Elsa.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Design.JavaScript.EventHandlers;

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
                .AddAsInterfaces<AddActivityOutputFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowFunctionDeclarationsProvider>()
                .AddAsInterfaces<AddWorkflowInputFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowVariableFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowVariablesDeclaration>()
                ;
        }
    }
}
