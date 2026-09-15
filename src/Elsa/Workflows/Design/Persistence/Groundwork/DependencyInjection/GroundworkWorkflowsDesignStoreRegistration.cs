using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
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
using Elsa.Tasks.Core;

namespace Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers the workflow-design ports against public Groundwork v2 storage units.</summary>
public static class GroundworkWorkflowsDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkWorkflowsDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existingBackend = DesignPersistenceBackend.Find(services);
        if (existingBackend is not null)
        {
            if (existingBackend.Name != DesignPersistenceBackend.Groundwork)
                throw new InvalidOperationException($"Workflow-design persistence backend '{existingBackend.Name}' is already selected; use an explicit replacement API to switch backends.");
            existingBackend.RemoveOwnedDescriptors(services);
        }
        else if (HasOwnedSurfaceRegistration(services))
            throw new InvalidOperationException("An explicit workflow-design persistence registration is already present; Groundwork refuses to replace it implicitly.");
        services.AddPersistenceCore();
        services.TryAddSingleton<IServiceCollection>(services);
        services.AddGroundworkStorageLane<WorkflowsDesignGroundworkStorageManifestSource>(targetName);
        foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, targetName);

        services.TryAddScoped<GroundworkDesignStorage>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName,
            provider.GetRequiredService<IGroundworkPrivilegedQueryAuditSink>()));
        services.TryAddScoped<GroundworkDesignAtomicWrite>();
        services.TryAddScoped<IDesignAtomicWriter>(provider =>
            provider.GetRequiredService<GroundworkDesignAtomicWrite>());
        services.TryAddScoped<IDraftOriginator, DraftOriginator>();

        ReplaceScoped<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>(services);
        services.RemoveAll<GroundworkWorkflowDefinitionVersionStore>();
        services.AddScoped<GroundworkWorkflowDefinitionVersionStore>();
        services.RemoveAll<IWorkflowDefinitionVersionStore>();
        services.AddScoped<IWorkflowDefinitionVersionStore>(provider =>
            provider.GetRequiredService<GroundworkWorkflowDefinitionVersionStore>());
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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateDesignPersistenceReplacementContractsStartupTask>());
        DesignPersistenceBackend.Register(services, new DesignPersistenceBackend(
            DesignPersistenceBackend.Groundwork,
            services.Where(IsOwnedDescriptor).ToArray()));
        return services;
    }

    private static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }

    private static bool IsOwnedDescriptor(ServiceDescriptor descriptor) =>
        OwnedServiceTypes.Contains(descriptor.ServiceType) &&
        (descriptor.ServiceType != typeof(IDesignAtomicWriter) || descriptor.ImplementationType == typeof(GroundworkDesignAtomicWrite));

    private static bool HasOwnedSurfaceRegistration(IServiceCollection services) => services.Any(descriptor =>
        OwnedServiceTypes.Contains(descriptor.ServiceType) &&
        descriptor.ServiceType != typeof(IDesignAtomicWriter) &&
        !IsFallbackDescriptor(descriptor));

    private static bool IsFallbackDescriptor(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { } implementationType &&
        typeof(IDesignPersistenceFallback).IsAssignableFrom(implementationType);

    private static readonly Type[] OwnedServiceTypes =
    [
        typeof(GroundworkDesignStorage),
        typeof(IDesignAtomicWriter),
        typeof(GroundworkDesignAtomicWrite),
        typeof(IWorkflowDefinitionLookup),
        typeof(IWorkflowDefinitionStore),
        typeof(IWorkflowDefinitionVersionStore),
        typeof(IWorkflowDefinitionDraftStore),
        typeof(IWorkflowDefinitionListProjectionStore),
        typeof(IWorkflowDefinitionVersionLayoutStore),
        typeof(IAddWorkflowDefinitionCommand),
        typeof(IMaterializeWorkflowDefinitionCommand),
        typeof(IAddWorkflowDefinitionVersionCommand),
        typeof(IMaterializeWorkflowDefinitionVersionCommand),
        typeof(ISaveWorkflowDefinitionCommand),
        typeof(IDeleteWorkflowDefinitionPermanentlyCommand),
        typeof(ICreateDraftCommand),
        typeof(IUpdateDraftCommand),
        typeof(IDiscardDraftCommand),
        typeof(IPromoteDraftToVersionCommand),
        typeof(ISubmitWorkflowDefinitionCommand),
        typeof(ICloneDraftFromVersionCommand),
        typeof(GroundworkWorkflowDefinitionStore),
        typeof(GroundworkWorkflowDefinitionVersionStore),
        typeof(GroundworkWorkflowDefinitionDraftStore),
        typeof(GroundworkWorkflowDefinitionListProjectionStore),
        typeof(GroundworkWorkflowDefinitionVersionLayoutStore),
        typeof(GroundworkAddWorkflowDefinitionCommand),
        typeof(GroundworkMaterializeWorkflowDefinitionCommand),
        typeof(GroundworkAddWorkflowDefinitionVersionCommand),
        typeof(GroundworkMaterializeWorkflowDefinitionVersionCommand),
        typeof(GroundworkSaveWorkflowDefinitionCommand),
        typeof(GroundworkDeleteWorkflowDefinitionPermanentlyCommand),
        typeof(GroundworkCreateDraftCommand),
        typeof(GroundworkUpdateDraftCommand),
        typeof(GroundworkDiscardDraftCommand),
        typeof(GroundworkPromoteDraftToVersionCommand),
        typeof(GroundworkSubmitWorkflowDefinitionCommand),
        typeof(GroundworkCloneDraftFromVersionCommand)
    ];
}
