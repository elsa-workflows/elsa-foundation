using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Workflows.Runtime.Services;
using Elsa.Agent.Workflows.Runtime.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Workflows.Runtime.Extensions;

public static class WorkflowRuntimeAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Runtime-side Weaver workflow contributions (spec 1193 slices F and G): the Executable and Run context
    /// sources, their capabilities and the read-only Executable and Run tools. They read Workflows Runtime Core
    /// (<c>IWorkflowExecutableInspection</c>, <c>IWorkflowRunInspection</c>) only and register no fallback for either; a missing
    /// owner is reported as unavailable. No mutating run tool is registered: retry, cancel, delete and dispatch stay with the run page.
    /// </summary>
    public static IServiceCollection AddFoundationWorkflowsRuntimeAgent(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, WorkflowRuntimeAgentCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, WorkflowExecutableContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowReadExecutableTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, WorkflowRunContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowReadRunTool>());
        return services;
    }
}
