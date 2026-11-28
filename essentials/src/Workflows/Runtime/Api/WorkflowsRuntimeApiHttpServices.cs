using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>The Runtime HTTP endpoint conventions and request authority shared by workflow execution backends.</summary>
internal static class WorkflowsRuntimeApiHttpServices
{
    internal static void AddWorkflowRuntimeApiHttpServices(this IServiceCollection services, bool includeLegacyUserTasks)
    {
        if (!includeLegacyUserTasks)
        {
            services.AddRetainedRuntimeHttpServices();
            return;
        }
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, WorkflowsRuntimeFaultRenderer>(WorkflowsRuntimeApi.OwnerId);
        services.AddRuntimeHttpServices<WorkflowsRuntimeJsonTypeInfoResolver, WorkflowRuntimePermissionContributor>();
    }

    internal static void AddExecutableInspectionServices(this IServiceCollection services)
    {
        services.TryAddScoped(sp => new WorkflowExecutableInspector(sp.GetRequiredService<IWorkflowExecutableStore>(),
            sp.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), sp.GetRequiredService<IWorkflowExecutableRetentionReader>(),
            sp.GetService<TimeProvider>()));
        services.TryAddScoped<WorkflowActivationSlotInspectionService>();
        services.TryAddScoped<IWorkflowExecutableInspector>(sp => sp.GetRequiredService<WorkflowExecutableInspector>());
        // The exact Executable read for other domains uses the same inspector as the HTTP endpoints.
        WorkflowExecutableInspectionStartupTask.Validate(services);
        services.TryAddScoped<IWorkflowExecutableInspection, WorkflowExecutableInspection>();
        AddExecutableInspectionValidator(services);
    }

    internal static void AddRunInspectionServices(this IServiceCollection services,
        Func<IServiceProvider, IWorkflowRunInspection>? create = null)
    {
        // Other domains use the endpoint's run services through the single exact Runtime Core contract.
        WorkflowRunInspectionStartupTask.Validate(services);
        if (create is null)
            services.TryAddScoped<IWorkflowRunInspection, WorkflowRunInspection>();
        else
            services.TryAddScoped(create);

        if (services.LastOrDefault(item => item.ServiceType == typeof(WorkflowRunInspectionStartupTask))
                ?.ImplementationInstance is not WorkflowRunInspectionStartupTask validator || !validator.Owns(services))
            validator = new WorkflowRunInspectionStartupTask(services);
        services.RemoveAll<WorkflowRunInspectionStartupTask>();
        services.AddSingleton(validator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is WorkflowRunInspectionStartupTask
                         || item.ImplementationType == typeof(WorkflowRunInspectionStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(validator));
    }

    // One validator per configuring collection: a copied child shell rebinds it to its own collection.
    private static void AddExecutableInspectionValidator(IServiceCollection services)
    {
        if (services.LastOrDefault(item => item.ServiceType == typeof(WorkflowExecutableInspectionStartupTask))
                ?.ImplementationInstance is not WorkflowExecutableInspectionStartupTask validator || !validator.Owns(services))
            validator = new WorkflowExecutableInspectionStartupTask(services);
        services.RemoveAll<WorkflowExecutableInspectionStartupTask>();
        services.AddSingleton(validator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is WorkflowExecutableInspectionStartupTask
                         || item.ImplementationType == typeof(WorkflowExecutableInspectionStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(validator));
    }
}
