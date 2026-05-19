using CShells.Features;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.JavaScript.EventHandlers;
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
                
                .AddAsInterfaces<AddWorkflowInputFunctions>()
                .AddAsInterfaces<AddActivityOutputFunctions>()
                .AddAsInterfaces<AddWorkflowFunctions>();
        }
    }
}
