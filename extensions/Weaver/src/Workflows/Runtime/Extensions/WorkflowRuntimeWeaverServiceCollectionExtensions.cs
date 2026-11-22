using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Workflows.Runtime.Services;
using Elsa.Weaver.Workflows.Runtime.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Workflows.Runtime.Extensions;

public static class WorkflowRuntimeWeaverServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Runtime-side Weaver workflow contributions (spec 1193 slices F and G): the Executable and Run context
    /// sources, their capabilities and the read-only Executable and Run tools. They read Workflows Runtime Core
    /// (<c>IWorkflowExecutableInspection</c>, <c>IWorkflowRunInspection</c>) only and register no fallback for either; a missing
    /// owner is reported as unavailable. No mutating run tool is registered: retry, cancel, delete and dispatch stay with the run page.
    /// </summary>
    public static IServiceCollection AddWeaverWorkflowsRuntime(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverCapabilityProvider, WorkflowRuntimeWeaverCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, WorkflowExecutableContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowReadExecutableTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, WorkflowRunContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowReadRunTool>());
        return services;
    }
}
