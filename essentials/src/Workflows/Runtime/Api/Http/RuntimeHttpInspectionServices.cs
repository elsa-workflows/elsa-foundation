using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>Registers the exact executable and run inspection used by the Runtime HTTP API and its consumers.</summary>
internal static class RuntimeHttpInspectionServices
{
    private static readonly LegacyRuntimeApiReaderRegistration ExecutableRetention = new(
        typeof(LegacyWorkflowExecutableRetentionReader),
        ServiceDescriptor.Scoped<IWorkflowExecutableRetentionReader>(static sp =>
            sp.GetRequiredService<LegacyWorkflowExecutableRetentionReader>()));

    private static readonly LegacyRuntimeApiReaderRegistration ActivityInspection = new(
        typeof(ActivityExecutionInspectionService),
        ServiceDescriptor.Scoped<IActivityExecutionInspectionService>(static sp =>
            sp.GetRequiredService<ActivityExecutionInspectionService>()));

    private static readonly LegacyRuntimeApiReaderRegistration ActivityBrowse = new(
        typeof(ActivityExecutionBrowseSource), ServiceDescriptor.Scoped<IActivityExecutionBrowseSource, ActivityExecutionBrowseSource>());

    internal static bool OwnsStoredReader(ServiceDescriptor descriptor) =>
        ExecutableRetention.Owns(descriptor) || ActivityInspection.Owns(descriptor) || ActivityBrowse.Owns(descriptor);

    internal static void AddLegacyExecutableRetentionReader(IServiceCollection services)
    {
        services.TryAddScoped<LegacyWorkflowExecutableRetentionReader>();
        services.TryAdd(ExecutableRetention.Default);
    }

    internal static void AddStoredActivityInspection(IServiceCollection services)
    {
        services.TryAddScoped<ActivityExecutionInspectionService>();
        services.TryAdd(ActivityInspection.Default);
        services.TryAdd(ActivityBrowse.Default);
    }

    internal static void AddExecutableInspectionServices(IServiceCollection services)
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

    internal static void AddRunInspectionServices(IServiceCollection services,
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
