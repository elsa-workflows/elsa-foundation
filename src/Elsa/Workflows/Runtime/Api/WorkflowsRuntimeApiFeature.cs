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
        services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();
        services.TryAddSingleton<IWorkflowExecutionCommandProcessor, WorkflowSchedulerCommandProcessor>();
        services.TryAddSingleton<IWorkflowSchedulerDrainer, WorkflowSchedulerDrainer>();
        services.TryAddSingleton<IWorkflowSchedulerDrainPolicy, ImmediateWorkflowSchedulerDrainPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainObserver, NoopWorkflowSchedulerDrainObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, NoopWorkflowSchedulerWorkHandler>());
        services.TryAddSingleton<IWorkflowExecutionAgentProvider, InProcessWorkflowExecutionAgentProvider>();
        services.TryAddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        services.TryAddScoped<IWorkflowExecutionStartDispatcher, WorkflowExecutionStartDispatcher>();
        services.AddRequestHandlersFrom(GetType().Assembly);
    }
}
