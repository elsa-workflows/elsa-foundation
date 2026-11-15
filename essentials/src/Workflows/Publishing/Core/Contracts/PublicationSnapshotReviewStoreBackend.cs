using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Records the selected owner of the publication snapshot-review store contract.</summary>
public sealed class PublicationSnapshotReviewStoreBackend
{
    public const string InMemory = "in-memory";
    public const string EntityFramework = "entity-framework";

    private readonly IReadOnlyCollection<ServiceDescriptor> descriptors;

    public PublicationSnapshotReviewStoreBackend(string name, ServiceDescriptor descriptor)
        : this(name, [descriptor])
    {
    }

    public PublicationSnapshotReviewStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors)
    {
        if (name is not (InMemory or EntityFramework))
            throw new ArgumentException($"Unknown publication snapshot-review store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0 || !this.descriptors.Any(descriptor => descriptor.ServiceType == typeof(IPublicationSnapshotReviewStore)))
            throw new ArgumentException("The owned descriptor must register IPublicationSnapshotReviewStore.", nameof(descriptors));
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor candidate) => descriptors.Contains(candidate);

    public static PublicationSnapshotReviewStoreBackend? Find(IServiceCollection services) => services
        .Select(service => service.ImplementationInstance)
        .OfType<PublicationSnapshotReviewStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, PublicationSnapshotReviewStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registrations = services.Where(service => service.ServiceType == typeof(IPublicationSnapshotReviewStore)).ToArray();
        if (registrations.Length != 1 || !Owns(registrations[0]) || descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Publication snapshot-review backend '{Name}' no longer exclusively owns its registration.");
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        foreach (var owned in descriptors)
            services.Remove(owned);
        for (var index = services.Count - 1; index >= 0; index--)
            if (ReferenceEquals(services[index].ImplementationInstance, this)) services.RemoveAt(index);
    }

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(IPublicationSnapshotReviewStore)))
            throw new InvalidOperationException("An explicit IPublicationSnapshotReviewStore registration is already present; the selected backend refuses to replace it implicitly.");
    }
}
