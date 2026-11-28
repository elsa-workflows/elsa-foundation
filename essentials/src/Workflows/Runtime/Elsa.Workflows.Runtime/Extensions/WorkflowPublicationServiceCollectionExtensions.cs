using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

public static class WorkflowPublicationServiceCollectionExtensions
{
    /// <summary>Registers the default stores and services for compiling and activating workflow publications.</summary>
    public static IServiceCollection AddWorkflowPublicationDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPersistenceCore();
        services.TryAddSingleton(TimeProvider.System);
        services.AddWorkflowPublicationTriggers();
        AddWorkflowArtifactDefaults(services);
        services.AddBuiltInIncidentStrategies();
        // The in-memory activation switch uses this shared state to reserve exclusive ingress in the slot commit.
        services.TryAddSingleton<InMemoryRuntimeCheckpointStoreState>();
        services.AddWorkflowActivationDefaults();
        return services;
    }

    internal static void AddWorkflowArtifactDefaults(IServiceCollection services)
    {
        // Own the three default native stores together. Factories never resolve a host's selected scoped
        // replacements into a singleton. Exact selected-store capability still rejects mixed compositions.
        var nativeArtifacts = ServiceDescriptor.Singleton<InMemoryRuntimeArtifactStores, InMemoryRuntimeArtifactStores>();
        if (RuntimeArtifactStoreBackend.Find(services)?.Name != RuntimeArtifactStoreBackend.EntityFramework)
            services.TryAdd(nativeArtifacts);
        var defaultWorkflowExecutableStore = ServiceDescriptor.Singleton<IWorkflowExecutableStore>(provider =>
            provider.GetRequiredService<InMemoryRuntimeArtifactStores>().Executables);
        services.TryAdd(defaultWorkflowExecutableStore);
        services.TryAddScoped<IWorkflowExecutableHasher, WorkflowExecutableHasher>();
        // One provider-neutral checker is shared by Publishing preflight and future Runtime artifact
        // import admission. Hosts compose the serialization/type-registry feature alongside Runtime.
        services.TryAddScoped<IRuntimeRequirementChecker, RuntimeRequirementChecker>();
        services.TryAddSingleton<IWorkflowArtifactClosureSerializer, WorkflowArtifactClosureSerializer>();
        var defaultActivityTemplateStore = ServiceDescriptor.Singleton<IExecutableActivityTemplateStore>(provider =>
            provider.GetRequiredService<InMemoryRuntimeArtifactStores>().Templates);
        services.TryAdd(defaultActivityTemplateStore);
        var defaultActivityTemplateReader = ServiceDescriptor.Scoped<IExecutableActivityTemplateReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IExecutableActivityTemplateStore>());
        services.TryAdd(defaultActivityTemplateReader);
        var defaultSourceReferenceStore = ServiceDescriptor.Singleton<IWorkflowExecutableSourceReferenceStore>(provider =>
            provider.GetRequiredService<InMemoryRuntimeArtifactStores>().References);
        services.TryAdd(defaultSourceReferenceStore);
        var defaultSourceReferenceReader = ServiceDescriptor.Scoped<IWorkflowExecutableSourceReferenceReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        services.TryAdd(defaultSourceReferenceReader);
        if (RuntimeArtifactStoreBackend.Find(services) is null)
        {
            var defaultArtifactDescriptors = services.Where(descriptor =>
                ReferenceEquals(descriptor, nativeArtifacts) ||
                ReferenceEquals(descriptor, defaultWorkflowExecutableStore) ||
                ReferenceEquals(descriptor, defaultActivityTemplateStore) ||
                ReferenceEquals(descriptor, defaultSourceReferenceStore) ||
                ReferenceEquals(descriptor, defaultActivityTemplateReader) ||
                ReferenceEquals(descriptor, defaultSourceReferenceReader)).ToArray();
            if (defaultArtifactDescriptors.Length == 6)
                RuntimeArtifactStoreBackend.Register(services, new RuntimeArtifactStoreBackend(RuntimeArtifactStoreBackend.InMemory, defaultArtifactDescriptors));
        }
        services.AddOptions<WorkflowExecutableGarbageCollectionOptions>();
        services.TryAddScoped<IWorkflowExecutableRootWriteLeaseManager, WorkflowExecutableRootWriteLeaseManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeDurableValueStorageDriver, JsonRuntimeDurableValueStorageDriver>());
        services.TryAddSingleton<IRuntimeDurableValueStorageDriverRegistry, RuntimeDurableValueStorageDriverRegistry>();
    }
}
