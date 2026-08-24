using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Attention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Opt-in clean-break registration for the current Groundwork v2 runtime attention query.</summary>
public static class GroundworkV2WorkflowRuntimeAttentionRegistration
{
    public static IServiceCollection AddGroundworkV2WorkflowRuntimeAttention(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var unitId in UnitIds)
            services.AddGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.Require(unitId), targetName);

        services.RemoveAll<GroundworkV2WorkflowRuntimeAttentionQuery>();
        services.RemoveAll<IWorkflowRuntimeAttentionQuery>();
        services.AddScoped<GroundworkV2WorkflowRuntimeAttentionQuery>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            provider.GetService<TimeProvider>(),
            targetName));
        services.AddScoped<IWorkflowRuntimeAttentionQuery>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowRuntimeAttentionQuery>());
        return services;
    }

    private static readonly string[] UnitIds =
    [
        ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
        ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind
    ];
}
