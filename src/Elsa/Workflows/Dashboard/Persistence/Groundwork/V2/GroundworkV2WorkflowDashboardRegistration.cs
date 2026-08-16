using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Core;
using Elsa.Workflows.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;

/// <summary>
/// Explicitly routes the dashboard run-health contract to the Groundwork v2 projection.
/// </summary>
/// <remarks>
/// This is an opt-in clean-break seam. The existing Groundwork dashboard feature continues to own its
/// v1 adapter until a host calls this method, at which point the v2 source replaces that registration.
/// </remarks>
public static class GroundworkV2WorkflowDashboardRegistration
{
    public static IServiceCollection AddGroundworkV2WorkflowRunHealth(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind),
            targetName);
        services.RemoveAll<IWorkflowRunHealthDataSource>();
        services.AddScoped<IWorkflowRunHealthDataSource>(provider =>
            new GroundworkV2WorkflowRunHealthDataSource(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                targetName));
        return services;
    }
}
