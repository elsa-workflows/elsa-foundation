using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the selected owner of the runtime bookmark-state and stimulus-index contracts.</summary>
public sealed class BookmarkStateStoreBackend
{
    private readonly ServiceDescriptor stateDescriptor;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    public BookmarkStateStoreBackend(
        string name,
        ServiceDescriptor stateDescriptor,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stateDescriptor);
        if (stateDescriptor.ServiceType != typeof(IBookmarkStateStore))
            throw new ArgumentException("The owned descriptor must register IBookmarkStateStore.", nameof(stateDescriptor));

        Name = name;
        this.stateDescriptor = stateDescriptor;
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
        .Any(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore)).ToArray();
        if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], stateDescriptor))
            throw new InvalidOperationException($"Bookmark state backend '{Name}' no longer exclusively owns IBookmarkStateStore.");
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        removeOwnedArtifacts?.Invoke(services);
    }
}
