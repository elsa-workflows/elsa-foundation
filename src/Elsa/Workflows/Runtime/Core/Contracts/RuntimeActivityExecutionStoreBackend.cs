using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the R07-R09 activity execution store registrations.</summary>
/// <remarks>
/// Runtime has several related contracts (state, inspection and hierarchy). Tracking them as one
/// backend prevents a provider adapter from silently replacing only part of the family.
/// </remarks>
public sealed class RuntimeActivityExecutionStoreBackend
{
    private static readonly Type[] ContractTypes =
    [
        typeof(IActivityExecutionStateStore),
        typeof(IActivityExecutionInspectionStore),
        typeof(IActivityExecutionInspectionWriter),
        typeof(IActivityExecutionHierarchyStore),
        typeof(IActivityExecutionHierarchyReader),
        typeof(IActivityExecutionHierarchyWriter)
    ];

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public const string InMemory = "in-memory";
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    public RuntimeActivityExecutionStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors, Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not InMemory and not Groundwork and not EntityFramework)
            throw new ArgumentException($"Unknown activity execution store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned activity execution descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static RuntimeActivityExecutionStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<RuntimeActivityExecutionStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, RuntimeActivityExecutionStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public static bool HasRegisteredContract(IServiceCollection services) => services.Any(IsSurfaceRegistration);

    /// <summary>
    /// Prevents switching between the Groundwork and EF activity-execution families while the current
    /// checkpoint writer still commits the R07-R09 units as one Groundwork transaction.
    /// </summary>
    public static void EnsureCheckpointCompositionCompatible(RuntimeActivityExecutionStoreBackend? existingBackend, string requestedBackend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBackend);
        if (existingBackend is not null && !StringComparer.Ordinal.Equals(existingBackend.Name, requestedBackend) &&
            existingBackend.Name is Groundwork or EntityFramework && requestedBackend is Groundwork or EntityFramework)
            throw new InvalidOperationException(
                "Activity execution Groundwork/EF switching is unavailable while the runtime checkpoint writer still commits R07-R09 through Groundwork; complete checkpoint ownership before switching this backend.");
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureSurfaceRegistrations(IServiceCollection services) => services.Where(IsSurfaceRegistration).ToArray();

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        if (services.Any(IsSurfaceRegistration))
            throw new InvalidOperationException("An explicit activity execution store registration is already present; the selected backend refuses to replace it implicitly.");
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Activity execution backend '{Name}' no longer owns one of its registrations.");
        foreach (var descriptor in services.Where(IsSurfaceRegistration))
        {
            if (!descriptors.Contains(descriptor))
                throw new InvalidOperationException($"Activity execution backend '{Name}' no longer exclusively owns {descriptor.ServiceType.Name}.");
        }
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            // EF keeps its auxiliary descriptors in the collection until the replacement
            // has been validated; its cleanup callback owns those descriptors. The
            // in-memory and Groundwork backends have no auxiliary service cleanup, so
            // withdraw every descriptor they own before the next registration adds its
            // concrete implementation.
            var providerOwnedDescriptors = Name == EntityFramework
                ? descriptors.Where(descriptor => ContractTypes.Contains(descriptor.ServiceType))
                : descriptors;
            foreach (var descriptor in providerOwnedDescriptors.Where(descriptor =>
                         BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                         RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                         WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                         RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                         WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                         RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true))
                services.Remove(descriptor);
            for (var index = services.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(services[index].ImplementationInstance, this))
                    services.RemoveAt(index);
            }
            return removeOwnedArtifacts;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        var snapshot = services.ToArray();
        try
        {
            PrepareRemoveOwnedArtifacts(services)?.Invoke(services);
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static bool IsSurfaceRegistration(ServiceDescriptor descriptor) => ContractTypes.Any(contract =>
        contract.IsAssignableFrom(descriptor.ServiceType) ||
        descriptor.ImplementationType is { } implementationType && contract.IsAssignableFrom(implementationType) ||
        descriptor.ImplementationInstance is { } implementationInstance && contract.IsAssignableFrom(implementationInstance.GetType()) ||
        descriptor.ImplementationFactory?.Method.ReturnType is { } returnType && contract.IsAssignableFrom(returnType));
}
