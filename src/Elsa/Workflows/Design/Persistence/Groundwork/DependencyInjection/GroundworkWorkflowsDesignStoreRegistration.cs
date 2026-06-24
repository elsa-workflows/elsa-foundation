using Elsa.Primitives.Contracts;
using Elsa.Persistence.Groundwork.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork (document) implementations of the workflow-design read ports. A provider
/// feature is responsible for registering the concrete <see cref="Groundwork.Documents.Store.IDocumentStore"/>
/// (and the host's <see cref="Elsa.Serialization.Core.IPayloadSerializer"/>) these adapters consume; this
/// method only swaps the read-port contracts over to the document-backed implementations, mirroring the
/// runtime lane's <c>AddGroundworkRuntimeStores</c>.
/// <para>
/// <see cref="ServiceCollectionDescriptorExtensions.RemoveAll{T}(IServiceCollection)"/> guarantees the
/// Groundwork adapters win over any previously-registered (e.g. EF Core) read stores regardless of feature
/// composition order, so a host that selects the Groundwork provider gets one provider backing every design
/// read.
/// </para>
/// </summary>
public static class GroundworkWorkflowsDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkWorkflowsDesignStores(this IServiceCollection services)
    {
        services.RemoveAll<IWorkflowDefinitionStore>();
        services.AddScoped<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>();

        services.RemoveAll<IWorkflowDefinitionVersionStore>();
        services.AddScoped<IWorkflowDefinitionVersionStore, GroundworkWorkflowDefinitionVersionStore>();

        services.RemoveAll<IWorkflowDefinitionDraftStore>();
        services.AddScoped<IWorkflowDefinitionDraftStore, GroundworkWorkflowDefinitionDraftStore>();

        services.RemoveAll<IWorkflowDefinitionVersionLayoutStore>();
        services.AddScoped<IWorkflowDefinitionVersionLayoutStore, GroundworkWorkflowDefinitionVersionLayoutStore>();

        services.RemoveAll<IAddWorkflowDefinitionCommand>();
        services.AddScoped<IAddWorkflowDefinitionCommand, GroundworkAddWorkflowDefinitionCommand>();

        services.RemoveAll<ISaveWorkflowDefinitionCommand>();
        services.AddScoped<ISaveWorkflowDefinitionCommand, GroundworkSaveWorkflowDefinitionCommand>();

        services.RemoveAll<IDeleteWorkflowDefinitionPermanentlyCommand>();
        services.AddScoped<IDeleteWorkflowDefinitionPermanentlyCommand, GroundworkDeleteWorkflowDefinitionPermanentlyCommand>();

        services.RemoveAll<ICreateDraftCommand>();
        services.AddScoped<ICreateDraftCommand, GroundworkCreateDraftCommand>();

        services.RemoveAll<IUpdateDraftCommand>();
        services.AddScoped<IUpdateDraftCommand, GroundworkUpdateDraftCommand>();

        services.RemoveAll<IDiscardDraftCommand>();
        services.AddScoped<IDiscardDraftCommand, GroundworkDiscardDraftCommand>();

        services.RemoveAll<IPromoteDraftToVersionCommand>();
        services.AddScoped<IPromoteDraftToVersionCommand, GroundworkPromoteDraftToVersionCommand>();

        services.RemoveAll<ISubmitWorkflowDefinitionCommand>();
        services.AddScoped<ISubmitWorkflowDefinitionCommand, GroundworkSubmitWorkflowDefinitionCommand>();

        services.RemoveAll<ICloneDraftFromVersionCommand>();
        services.AddScoped<ICloneDraftFromVersionCommand, GroundworkCloneDraftFromVersionCommand>();

        services.RemoveAll<IWorkflowDefinitionLookup>();
        services.AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>();

        services.RemoveAll<IDraftStateDiffEngine>();
        services.AddScoped<IDraftStateDiffEngine, DraftStateDiffEngine>();

        services.TryAddScoped<IIdentityGenerator, GroundworkIdentityGenerator>();
        services.TryAddScoped<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddScoped<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();

        return services;
    }
}
