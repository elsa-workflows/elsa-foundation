using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

public static class WorkflowPublicationServiceCollectionExtensions
{
    /// <summary>Registers the default stores and services for compiling and activating workflow publications.</summary>
    public static IServiceCollection AddWorkflowPublicationDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        WorkflowPublicationDefaults.AddSharedServices(services);
        // The in-memory activation switch uses this shared state to reserve exclusive ingress in the slot commit.
        services.TryAddSingleton<InMemoryRuntimeCheckpointStoreState>();
        services.AddWorkflowActivationDefaults();
        return services;
    }

    internal static void AddWorkflowArtifactDefaults(IServiceCollection services) =>
        WorkflowPublicationArtifactDefaults.AddWorkflowArtifactDefaults(services);
}
