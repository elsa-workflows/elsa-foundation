using CShells.Features;
using Elsa.Mediator.Core.Extensions;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.JavaScript.EventHandlers;
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
                .AddDomainEventHandlersFrom(typeof(JavaScriptWorkflowsDesignFeature).Assembly)

                .AddAsInterfaces<AddActivityOutputFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowFunctionDeclarationsProvider>()
                .AddAsInterfaces<AddWorkflowInputFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowVariableFunctionDeclarations>()
                .AddAsInterfaces<AddWorkflowVariablesDeclaration>()
                ;
        }
    }
}
