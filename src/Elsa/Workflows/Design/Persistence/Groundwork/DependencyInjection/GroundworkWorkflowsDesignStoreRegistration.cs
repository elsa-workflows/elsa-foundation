using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork (document) implementations of the workflow-design read ports against one named
/// Groundwork target. A provider feature is responsible for declaring that target and the host's
/// <see cref="Elsa.Serialization.Core.IPayloadSerializer"/>; this method only swaps the read-port contracts
/// over to the document-backed implementations and binds them to that target's store.
/// <para>
/// Naming a target other than the default is what puts the authoring catalog in its own database. The design
/// lane's storage manifests are bound to the same target, so that database carries design tables and nothing
/// else.
/// </para>
/// <para>
/// The registrar replaces rather than tries-to-add, so the Groundwork adapters win over any previously
/// registered read stores regardless of feature composition order.
/// </para>
/// </summary>
public static class GroundworkWorkflowsDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkWorkflowsDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        services.AddPersistenceCore();
        var lane = services.GroundworkLane(targetName);

        lane.Manifest<WorkflowsDesignGroundworkStorageManifestSource>();
        lane.Manifest<GroundworkDesignAtomicWriteStorageManifestSource>();
        lane.TryAdd<IDesignAtomicWriter, GroundworkDesignAtomicWrite>();
        services.TryAddScoped<IDraftOriginator, DraftOriginator>();

        lane.Replace<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>();
        lane.Replace<IWorkflowDefinitionVersionStore, GroundworkWorkflowDefinitionVersionStore>();
        lane.Replace<IWorkflowDefinitionDraftStore, GroundworkWorkflowDefinitionDraftStore>();
        lane.Replace<IWorkflowDefinitionListProjectionStore, GroundworkWorkflowDefinitionListProjectionStore>();
        lane.Replace<IWorkflowDefinitionVersionLayoutStore, GroundworkWorkflowDefinitionVersionLayoutStore>();
        lane.Replace<IAddWorkflowDefinitionCommand, GroundworkAddWorkflowDefinitionCommand>();
        lane.Replace<IMaterializeWorkflowDefinitionCommand, GroundworkMaterializeWorkflowDefinitionCommand>();
        lane.Replace<IAddWorkflowDefinitionVersionCommand, GroundworkAddWorkflowDefinitionVersionCommand>();
        lane.Replace<IMaterializeWorkflowDefinitionVersionCommand, GroundworkMaterializeWorkflowDefinitionVersionCommand>();
        lane.Replace<ISaveWorkflowDefinitionCommand, GroundworkSaveWorkflowDefinitionCommand>();
        lane.Replace<IDeleteWorkflowDefinitionPermanentlyCommand, GroundworkDeleteWorkflowDefinitionPermanentlyCommand>();
        lane.Replace<ICreateDraftCommand, GroundworkCreateDraftCommand>();
        lane.Replace<IUpdateDraftCommand, GroundworkUpdateDraftCommand>();
        lane.Replace<IDiscardDraftCommand, GroundworkDiscardDraftCommand>();
        lane.Replace<IPromoteDraftToVersionCommand, GroundworkPromoteDraftToVersionCommand>();
        lane.Replace<ISubmitWorkflowDefinitionCommand, GroundworkSubmitWorkflowDefinitionCommand>();
        lane.Replace<ICloneDraftFromVersionCommand, GroundworkCloneDraftFromVersionCommand>();

        // The lookup composes the design ports above rather than a store, so it needs no target binding.
        services.RemoveAll<IWorkflowDefinitionLookup>();
        services.AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>();

        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddScoped<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();
        services.TryAddScoped<IWorkflowDefinitionDraftFactory, WorkflowDefinitionDraftFactory>();

        return services;
    }
}
