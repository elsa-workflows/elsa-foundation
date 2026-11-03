using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Workflows.Runtime.Services;
using Elsa.Agent.Workflows.Runtime.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Workflows.Runtime.Extensions;

public static class WorkflowRuntimeAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Runtime-side Weaver workflow contributions (spec 1193 slice F): the Executable context source, its
    /// capability and the read-only Executable tool. They read Workflows Runtime Core (<c>IWorkflowExecutableInspection</c>)
    /// only and register no fallback for it; a missing owner is reported as unavailable.
    /// </summary>
    public static IServiceCollection AddFoundationWorkflowsRuntimeAgent(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, WorkflowRuntimeAgentCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, WorkflowExecutableContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowReadExecutableTool>());
        return services;
    }
}
