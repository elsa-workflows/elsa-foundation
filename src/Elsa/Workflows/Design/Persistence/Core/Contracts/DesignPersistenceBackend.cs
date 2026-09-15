using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>Tracks the exact service descriptors owned by the selected design persistence backend.</summary>
public sealed class DesignPersistenceBackend
{
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    /// <summary>Provider-neutral design persistence contracts that select exactly one implementation.</summary>
    public static IReadOnlySet<Type> ReplacementContractTypes { get; } = new HashSet<Type>
    {
        typeof(Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionFactory),
        typeof(Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionDraftFactory),
        typeof(Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionVersionFactory),
        typeof(Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionLookup),
        typeof(Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionStore),
        typeof(Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore),
        typeof(Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionDraftStore),
        typeof(Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionLayoutStore),
        typeof(Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionListProjectionStore),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IDesignAtomicWriter),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IAddWorkflowDefinitionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IAddWorkflowDefinitionVersionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.ICreateDraftCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.ICloneDraftFromVersionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IDeleteWorkflowDefinitionPermanentlyCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IDiscardDraftCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IMaterializeWorkflowDefinitionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IMaterializeWorkflowDefinitionVersionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IPromoteDraftToVersionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.ISaveWorkflowDefinitionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.ISubmitWorkflowDefinitionCommand),
        typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IUpdateDraftCommand)
    };

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;

    public DesignPersistenceBackend(string name, IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned design-persistence descriptor is required.", nameof(descriptors));
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    /// <summary>Rejects a missing owned registration or an unowned replacement contract.
    /// Shared host infrastructure and option descriptors remain additive.
    /// </summary>
    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        EnsureOwnedDescriptorsPresent(services);
        foreach (var serviceType in descriptors.Select(descriptor => descriptor.ServiceType)
                     .Where(ReplacementContractTypes.Contains).Distinct())
        {
            if (services.Any(descriptor => descriptor.ServiceType == serviceType && !Owns(descriptor)))
                throw new InvalidOperationException($"Design persistence backend '{Name}' no longer exclusively owns {serviceType.Name}.");
        }
    }

    public static DesignPersistenceBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<DesignPersistenceBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, DesignPersistenceBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public void RemoveOwnedDescriptors(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureOwnsRegisteredDescriptors(services);
        var snapshot = services.ToArray();
        try
        {
            foreach (var descriptor in descriptors)
                services.Remove(descriptor);
            for (var index = services.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(services[index].ImplementationInstance, this))
                    services.RemoveAt(index);
            }
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private void EnsureOwnedDescriptorsPresent(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Design persistence backend '{Name}' no longer owns one of its registrations.");
    }

    private void EnsureOwnsRegisteredDescriptors(IServiceCollection services)
    {
        EnsureOwnedDescriptorsPresent(services);
        foreach (var serviceType in descriptors.Select(descriptor => descriptor.ServiceType).Distinct())
        {
            var registrations = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
            if (registrations.Any(registration => !descriptors.Contains(registration)))
                throw new InvalidOperationException($"Design persistence backend '{Name}' no longer exclusively owns {serviceType.Name}.");
        }
    }
}
