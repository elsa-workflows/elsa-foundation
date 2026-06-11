using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api;

[ShellFeature(
    name: "WorkflowsRuntimeApi",
    Description = "Runtime workflow execution endpoints for published WorkflowExecutable artifacts."
)]
public class WorkflowsRuntimeApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutionCommandProcessor, NoopWorkflowExecutionCommandProcessor>();
        services.TryAddSingleton<IWorkflowExecutionAgentProvider, InProcessWorkflowExecutionAgentProvider>();
        services.TryAddScoped<IWorkflowExecutor, SequentialWorkflowExecutor>();
        services.AddRequestHandlersFrom(GetType().Assembly);
    }
}
