using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Design.Persistence.Groundwork;
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

    /// <summary>
    /// Routes the dashboard portfolio contract to the Groundwork v2 projection.
    /// </summary>
    /// <remarks>
    /// The tile spans two lanes: definitions and drafts on the design lane, live published source references
    /// on the runtime lane. Both are named here so a host that puts them in different databases needs no
    /// further wiring — each unit resolves through its own target.
    /// </remarks>
    public static IServiceCollection AddGroundworkV2WorkflowPortfolio(
        this IServiceCollection services,
        string? designTargetName = null,
        string? runtimeTargetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGroundworkStorageUnit(
            WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
            designTargetName);
        services.AddGroundworkStorageUnit(
            WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
            designTargetName);
        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind),
            runtimeTargetName);
        services.RemoveAll<IWorkflowPortfolioDataSource>();
        services.AddScoped<IWorkflowPortfolioDataSource>(provider =>
            new GroundworkV2WorkflowPortfolioDataSource(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetRequiredService<IPayloadSerializer>(),
                designTargetName,
                runtimeTargetName));
        return services;
    }
}
