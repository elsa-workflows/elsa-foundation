using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the selected owner of the runtime bookmark-state and stimulus-index contracts.</summary>
public sealed class BookmarkStateStoreBackend
{
    private readonly ServiceDescriptor stateDescriptor;
    private readonly ServiceDescriptor indexDescriptor;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    public BookmarkStateStoreBackend(
        string name,
        ServiceDescriptor stateDescriptor,
        ServiceDescriptor indexDescriptor,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
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
    }

    public string Name { get; }

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

    /// <summary>Registers the Runtime-owned bridge from the selected bookmark store to its stimulus index.</summary>
    public static void TryRegisterDefaultStimulusIndex(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
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
        var registrations = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DefaultStimulusIndexRegistration>()
            .ToArray();
        var indexDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex))
            .ToArray();

        if (registrations.Length == 0)
        {
            if (indexDescriptors.Length > 0)
                throw new InvalidOperationException("An explicit IBookmarkStimulusIndex is already registered; persistence refuses to replace it implicitly.");
            return;
        }

        if (registrations.Length != 1 || indexDescriptors.Length != 1 ||
            !ReferenceEquals(registrations[0].Descriptor, indexDescriptors[0]))
            throw new InvalidOperationException("The Runtime-owned IBookmarkStimulusIndex bridge no longer exclusively owns the contract.");

        services.Remove(registrations[0].Descriptor);
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(services[index].ImplementationInstance, registrations[0]))
                services.RemoveAt(index);
        }
    }

    public static void EnsureKnown(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not Groundwork and not EntityFramework)
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
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        services.Remove(stateDescriptor);
        services.Remove(indexDescriptor);
        removeOwnedArtifacts?.Invoke(services);
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(services[index].ImplementationInstance, this))
                services.RemoveAt(index);
        }
    }

    private sealed record DefaultStimulusIndexRegistration(ServiceDescriptor Descriptor);
}
