using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the selected owner of the runtime bookmark-state and stimulus-index contracts.</summary>
public sealed class BookmarkStateStoreBackend
{
    private readonly ServiceDescriptor stateDescriptor;
    private readonly ServiceDescriptor indexDescriptor;
    private readonly IReadOnlyCollection<ServiceDescriptor> auxiliaryDescriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public const string EntityFramework = "entity-framework";

    public BookmarkStateStoreBackend(
        string name,
        ServiceDescriptor stateDescriptor,
        ServiceDescriptor indexDescriptor,
        Action<IServiceCollection>? removeOwnedArtifacts = null,
        IEnumerable<ServiceDescriptor>? auxiliaryDescriptors = null)
    {
        EnsureKnown(name);
        ArgumentNullException.ThrowIfNull(stateDescriptor);
        if (stateDescriptor.ServiceType != typeof(IBookmarkStateStore))
            throw new ArgumentException("The owned descriptor must register IBookmarkStateStore.", nameof(stateDescriptor));
        ArgumentNullException.ThrowIfNull(indexDescriptor);
        if (indexDescriptor.ServiceType != typeof(IBookmarkStimulusIndex))
            throw new ArgumentException("The owned descriptor must register IBookmarkStimulusIndex.", nameof(indexDescriptor));

        Name = name;
        this.stateDescriptor = stateDescriptor;
        this.indexDescriptor = indexDescriptor;
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        this.auxiliaryDescriptors = (auxiliaryDescriptors ?? []).ToArray();
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) =>
        ReferenceEquals(descriptor, stateDescriptor) ||
        ReferenceEquals(descriptor, indexDescriptor) ||
        auxiliaryDescriptors.Contains(descriptor);

    public static void Register(IServiceCollection services, BookmarkStateStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public static BookmarkStateStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<BookmarkStateStoreBackend>()
        .SingleOrDefault();

    public static bool HasRegisteredContract(IServiceCollection services) => services
        .Any(descriptor => descriptor.ServiceType is { } serviceType &&
                           (serviceType == typeof(IBookmarkStateStore) || serviceType == typeof(IBookmarkStimulusIndex)));

    /// <summary>Registers the Runtime-owned in-memory bookmark store without claiming an explicit host registration.</summary>
    public static void TryRegisterDefaultStateStore(IServiceCollection services, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ServiceType != typeof(IBookmarkStateStore))
            throw new ArgumentException("The default descriptor must register IBookmarkStateStore.", nameof(descriptor));

        var registrations = DefaultStateStoreRegistrations(services);
        if (registrations.Length > 0)
        {
            EnsureDefaultStateStoreOwnership(services, registrations);
            return;
        }

        if (services.Any(candidate => candidate.ServiceType == typeof(IBookmarkStateStore)))
            return;

        services.Add(descriptor);
        services.AddSingleton(new DefaultStateStoreRegistration(descriptor));
    }

    /// <summary>Registers the Runtime-owned bridge from the selected bookmark store to its stimulus index.</summary>
    public static void TryRegisterDefaultStimulusIndex(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registrations = DefaultStimulusIndexRegistrations(services);
        if (registrations.Length > 0)
        {
            EnsureDefaultStimulusIndexOwnership(services, registrations);
            return;
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex)))
            return;

        var descriptor = ServiceDescriptor.Scoped<IBookmarkStimulusIndex>(provider =>
            (IBookmarkStimulusIndex)provider.GetRequiredService<IBookmarkStateStore>());
        services.Add(descriptor);
        services.AddSingleton(new DefaultStimulusIndexRegistration(descriptor));
    }

    /// <summary>Removes the Runtime-owned default bridge, refusing to remove an unowned index.</summary>
    public static void RemoveDefaultStimulusIndex(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registrations = DefaultStimulusIndexRegistrations(services);

        EnsureDefaultStimulusIndexOwnership(services, registrations);

        if (registrations.Length == 0)
            return;

        services.Remove(registrations[0].Descriptor);
        RemoveMarker(services, registrations[0]);
    }

    /// <summary>Removes the Runtime-owned in-memory bookmark store, refusing to remove an explicit store.</summary>
    public static void RemoveDefaultStateStore(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registrations = DefaultStateStoreRegistrations(services);

        EnsureDefaultStateStoreOwnership(services, registrations);

        if (registrations.Length == 0)
            return;

        services.Remove(registrations[0].Descriptor);
        RemoveMarker(services, registrations[0]);
    }

    /// <summary>Validates that any unselected bookmark contracts are Runtime-owned defaults before replacement.</summary>
    public static void EnsureRuntimeDefaultsOwnRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureDefaultStateStoreOwnership(services, DefaultStateStoreRegistrations(services));
        EnsureDefaultStimulusIndexOwnership(services, DefaultStimulusIndexRegistrations(services));
    }

    public static void EnsureKnown(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not EntityFramework)
            throw new ArgumentException($"Unknown bookmark state store backend '{name}'.", nameof(name));
    }

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore)).ToArray();
        if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], stateDescriptor))
            throw new InvalidOperationException($"Bookmark state backend '{Name}' no longer exclusively owns IBookmarkStateStore.");
        var indexDescriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex)).ToArray();
        if (indexDescriptors.Length != 1 || !ReferenceEquals(indexDescriptors[0], indexDescriptor))
            throw new InvalidOperationException($"Bookmark state backend '{Name}' no longer exclusively owns IBookmarkStimulusIndex.");

        if (auxiliaryDescriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Bookmark state backend '{Name}' no longer exclusively owns its auxiliary registrations.");
    }

    /// <summary>Validates ownership and rejects additional registrations for the same auxiliary service types.</summary>
    public void EnsureOwnsRegisteredAuxiliaryContracts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        var auxiliaryTypes = auxiliaryDescriptors
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();
        if (services.Any(descriptor => auxiliaryTypes.Contains(descriptor.ServiceType) && !auxiliaryDescriptors.Contains(descriptor)))
            throw new InvalidOperationException($"Bookmark state backend '{Name}' no longer exclusively owns its auxiliary registrations.");
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        var snapshot = services.ToArray();
        var commit = PrepareRemoveOwnedArtifacts(services);
        try
        {
            commit?.Invoke(services);
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    /// <summary>Removes public contracts and returns auxiliary cleanup to invoke after replacement validation succeeds.</summary>
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureOwnsRegisteredContract(services);
        var snapshot = services.ToArray();
        try
        {
            services.Remove(stateDescriptor);
            services.Remove(indexDescriptor);
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

    private sealed record DefaultStimulusIndexRegistration(ServiceDescriptor Descriptor);
    private sealed record DefaultStateStoreRegistration(ServiceDescriptor Descriptor);

    private static DefaultStateStoreRegistration[] DefaultStateStoreRegistrations(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<DefaultStateStoreRegistration>()
        .ToArray();

    private static DefaultStimulusIndexRegistration[] DefaultStimulusIndexRegistrations(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<DefaultStimulusIndexRegistration>()
        .ToArray();

    private static void EnsureDefaultStateStoreOwnership(
        IServiceCollection services,
        IReadOnlyCollection<DefaultStateStoreRegistration> registrations)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore)).ToArray();
        if (registrations.Count == 0)
        {
            if (descriptors.Length > 0)
                throw new InvalidOperationException("An explicit IBookmarkStateStore is already registered; persistence refuses to replace it implicitly.");
            return;
        }

        if (registrations.Count != 1 || descriptors.Length != 1)
            throw new InvalidOperationException("The Runtime-owned IBookmarkStateStore default no longer exclusively owns the contract.");
        var registration = registrations.Single();
        if (!ReferenceEquals(registration.Descriptor, descriptors[0]))
            throw new InvalidOperationException("The Runtime-owned IBookmarkStateStore default no longer exclusively owns the contract.");
    }

    private static void EnsureDefaultStimulusIndexOwnership(
        IServiceCollection services,
        IReadOnlyCollection<DefaultStimulusIndexRegistration> registrations)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex)).ToArray();
        if (registrations.Count == 0)
        {
            if (descriptors.Length > 0)
                throw new InvalidOperationException("An explicit IBookmarkStimulusIndex is already registered; persistence refuses to replace it implicitly.");
            return;
        }

        if (registrations.Count != 1 || descriptors.Length != 1)
            throw new InvalidOperationException("The Runtime-owned IBookmarkStimulusIndex bridge no longer exclusively owns the contract.");
        var registration = registrations.Single();
        if (!ReferenceEquals(registration.Descriptor, descriptors[0]))
            throw new InvalidOperationException("The Runtime-owned IBookmarkStimulusIndex bridge no longer exclusively owns the contract.");
    }

    private static void RemoveMarker(IServiceCollection services, object marker)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(services[index].ImplementationInstance, marker))
                services.RemoveAt(index);
        }
    }
}
