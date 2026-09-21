using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Tracks the exact registrations owned by the selected policy/projection-intent backend.</summary>
/// <remarks>
/// Policy and projection-intent stores are one persistence composition. Keeping their ownership marker
/// separate from the P04 snapshot-review marker lets a backend preserve a snapshot-review store while
/// replacing (or preserving) the P02/P03 stores, without inferring ownership from implementation types.
/// </remarks>
public sealed class PublicationPolicyProjectionStoreBackend
{
    public const string InMemory = "in-memory";
    public const string EntityFramework = "entity-framework";

    private static readonly Type[] ContractTypes =
    [
        typeof(IPublicationPolicyStore),
        typeof(IPublicationProjectionIntentStore)
    ];

    private readonly IReadOnlyCollection<ServiceDescriptor> descriptors;

    public PublicationPolicyProjectionStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors)
    {
        if (name is not (InMemory or EntityFramework))
            throw new ArgumentException($"Unknown publication policy/projection-intent store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0 || ContractTypes.Any(contract => this.descriptors.All(descriptor => descriptor.ServiceType != contract)))
            throw new ArgumentException("The owned descriptors must register both publication policy and projection-intent contracts.", nameof(descriptors));
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor candidate) => descriptors.Contains(candidate);

    public static PublicationPolicyProjectionStoreBackend? Find(IServiceCollection services) => services
        .Select(service => service.ImplementationInstance)
        .OfType<PublicationPolicyProjectionStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, PublicationPolicyProjectionStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Publication policy/projection-intent backend '{Name}' no longer owns one of its registrations.");

        foreach (var contract in ContractTypes)
        {
            var registrations = services.Where(service => service.ServiceType == contract).ToArray();
            if (registrations.Length != 1 || !Owns(registrations[0]))
                throw new InvalidOperationException($"Publication policy/projection-intent backend '{Name}' no longer exclusively owns its {contract.Name} registration.");
        }
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        foreach (var owned in descriptors)
            services.Remove(owned);
        for (var index = services.Count - 1; index >= 0; index--)
            if (ReferenceEquals(services[index].ImplementationInstance, this))
                services.RemoveAt(index);
    }

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => ContractTypes.Contains(service.ServiceType)))
            throw new InvalidOperationException("An explicit publication policy or projection-intent store registration is already present; the selected backend refuses to replace it implicitly.");
    }
}
