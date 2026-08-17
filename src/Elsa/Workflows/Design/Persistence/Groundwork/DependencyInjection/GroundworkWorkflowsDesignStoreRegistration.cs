using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers the workflow-design ports against public Groundwork v2 storage units.</summary>
public static class GroundworkWorkflowsDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkWorkflowsDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, targetName);

        services.TryAddScoped<GroundworkDesignStorage>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName,
            provider.GetRequiredService<IGroundworkPrivilegedQueryAuditSink>()));
        services.TryAddScoped<IDesignAtomicWriter, GroundworkDesignAtomicWrite>();
        services.TryAddScoped<IDraftOriginator, DraftOriginator>();

        ReplaceScoped<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>(services);
        ReplaceScoped<IWorkflowDefinitionVersionStore, GroundworkWorkflowDefinitionVersionStore>(services);
        ReplaceScoped<IWorkflowDefinitionDraftStore, GroundworkWorkflowDefinitionDraftStore>(services);
        ReplaceScoped<IWorkflowDefinitionListProjectionStore, GroundworkWorkflowDefinitionListProjectionStore>(services);
        ReplaceScoped<IWorkflowDefinitionVersionLayoutStore, GroundworkWorkflowDefinitionVersionLayoutStore>(services);
        ReplaceScoped<IAddWorkflowDefinitionCommand, GroundworkAddWorkflowDefinitionCommand>(services);
        ReplaceScoped<IMaterializeWorkflowDefinitionCommand, GroundworkMaterializeWorkflowDefinitionCommand>(services);
        ReplaceScoped<IAddWorkflowDefinitionVersionCommand, GroundworkAddWorkflowDefinitionVersionCommand>(services);
        ReplaceScoped<IMaterializeWorkflowDefinitionVersionCommand, GroundworkMaterializeWorkflowDefinitionVersionCommand>(services);
        ReplaceScoped<ISaveWorkflowDefinitionCommand, GroundworkSaveWorkflowDefinitionCommand>(services);
        ReplaceScoped<IDeleteWorkflowDefinitionPermanentlyCommand, GroundworkDeleteWorkflowDefinitionPermanentlyCommand>(services);
        ReplaceScoped<ICreateDraftCommand, GroundworkCreateDraftCommand>(services);
        ReplaceScoped<IUpdateDraftCommand, GroundworkUpdateDraftCommand>(services);
        ReplaceScoped<IDiscardDraftCommand, GroundworkDiscardDraftCommand>(services);
        ReplaceScoped<IPromoteDraftToVersionCommand, GroundworkPromoteDraftToVersionCommand>(services);
        ReplaceScoped<ISubmitWorkflowDefinitionCommand, GroundworkSubmitWorkflowDefinitionCommand>(services);
        ReplaceScoped<ICloneDraftFromVersionCommand, GroundworkCloneDraftFromVersionCommand>(services);

        services.RemoveAll<IWorkflowDefinitionLookup>();
        services.AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>();
        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddScoped<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();
        services.TryAddScoped<IWorkflowDefinitionDraftFactory, WorkflowDefinitionDraftFactory>();
        return services;
    }

    private static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }
}
