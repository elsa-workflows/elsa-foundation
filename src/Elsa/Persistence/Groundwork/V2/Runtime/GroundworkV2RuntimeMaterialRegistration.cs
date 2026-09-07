using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Registers the current-only executable, template, and source-reference Groundwork v2 family.</summary>
public static class GroundworkV2RuntimeMaterialRegistration
{
    /// <summary>Registers the current-only v2 alteration plan/job ledger.</summary>
    public static IServiceCollection AddGroundworkV2WorkflowAlterationStore(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind),
            targetName);
        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind),
            targetName);
        services.RemoveAll<GroundworkV2WorkflowAlterationStore>();
        services.RemoveAll<IWorkflowAlterationStore>();
        services.AddScoped<GroundworkV2WorkflowAlterationStore>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.AddScoped<IWorkflowAlterationStore>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowAlterationStore>());
        return services;
    }

    public static IServiceCollection AddGroundworkV2RuntimeMaterials(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var unitId in UnitIds)
            services.AddGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.Require(unitId), targetName);

        services.RemoveAll<GroundworkV2WorkflowExecutableStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddScoped<GroundworkV2WorkflowExecutableStore>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.AddScoped<IWorkflowExecutableStore>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableStore>());

        services.RemoveAll<GroundworkV2ExecutableActivityTemplateStore>();
        services.RemoveAll<IExecutableActivityTemplateStore>();
        services.RemoveAll<IExecutableActivityTemplateReader>();
        services.RemoveAll<IExecutableActivityTemplateWriter>();
        services.AddScoped<GroundworkV2ExecutableActivityTemplateStore>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.AddScoped<IExecutableActivityTemplateStore>(provider =>
            provider.GetRequiredService<GroundworkV2ExecutableActivityTemplateStore>());
        services.AddScoped<IExecutableActivityTemplateReader>(provider =>
            provider.GetRequiredService<GroundworkV2ExecutableActivityTemplateStore>());
        services.AddScoped<IExecutableActivityTemplateWriter>(provider =>
            provider.GetRequiredService<GroundworkV2ExecutableActivityTemplateStore>());

        services.RemoveAll<GroundworkV2WorkflowExecutableSourceReferenceStore>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceStore>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceReader>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceWriter>();
        services.AddScoped<GroundworkV2WorkflowExecutableSourceReferenceStore>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.AddScoped<IWorkflowExecutableSourceReferenceStore>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableSourceReferenceStore>());
        services.AddScoped<IWorkflowExecutableSourceReferenceReader>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableSourceReferenceStore>());
        services.AddScoped<IWorkflowExecutableSourceReferenceWriter>(provider =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableSourceReferenceStore>());
        return services;
    }

    private static readonly string[] UnitIds =
    [
        ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];
}
