using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks the exact registrations owned by the selected runtime-artifact backend.</summary>
/// <remarks>
/// Ownership is represented by the descriptor instances themselves. This deliberately avoids inferring ownership
/// from implementation namespaces, which would allow an unrelated host registration to be removed accidentally.
/// </remarks>
public sealed class RuntimeArtifactStoreBackend
{
    private static readonly Type[] ArtifactContractTypes =
    [
        typeof(IWorkflowExecutableStore),
        typeof(IExecutableActivityTemplateStore),
        typeof(IExecutableActivityTemplateReader),
        typeof(IExecutableActivityTemplateWriter),
        typeof(IWorkflowExecutableSourceReferenceStore),
        typeof(IWorkflowExecutableSourceReferenceReader),
        typeof(IWorkflowExecutableSourceReferenceWriter)
    ];
    public const string InMemory = "in-memory";
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public RuntimeArtifactStoreBackend(
        string name,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not InMemory and not Groundwork and not EntityFramework)
            throw new ArgumentException($"Unknown runtime artifact store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned runtime artifact descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static RuntimeArtifactStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<RuntimeArtifactStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, RuntimeArtifactStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var owned in descriptors)
        {
            if (!services.Contains(owned))
                throw new InvalidOperationException($"Runtime artifact backend '{Name}' no longer owns one of its registrations.");
        }

        foreach (var descriptor in services.Where(IsArtifactSurfaceRegistration))
        {
            if (!descriptors.Contains(descriptor))
                throw new InvalidOperationException($"Runtime artifact backend '{Name}' no longer exclusively owns the runtime artifact registration for {descriptor.ServiceType.Name}.");
        }
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureArtifactSurfaceRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Where(IsArtifactSurfaceRegistration).ToArray();
    }

    /// <summary>Rejects host registrations that no selected runtime-artifact backend can safely replace.</summary>
    public static void EnsureNoUnownedArtifactRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(IsArtifactSurfaceRegistration))
            throw new InvalidOperationException("An explicit runtime artifact store registration is already present; the selected backend refuses to replace it implicitly.");
    }

    private static bool IsArtifactSurfaceRegistration(ServiceDescriptor descriptor) =>
        ArtifactContractTypes.Any(contract =>
            contract.IsAssignableFrom(descriptor.ServiceType) ||
            descriptor.ImplementationType is { } implementationType && contract.IsAssignableFrom(implementationType) ||
            descriptor.ImplementationInstance is { } implementationInstance && contract.IsAssignableFrom(implementationInstance.GetType()) ||
            descriptor.ImplementationFactory?.Method.ReturnType is { } returnType && contract.IsAssignableFrom(returnType));

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

    /// <summary>Removes owned descriptors and returns cleanup to invoke after replacement validation succeeds.</summary>
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            foreach (var descriptor in descriptors)
            {
                // Bookmark EF may reuse this context and records the same descriptor as a sibling owner.
                // Keep it alive while replacing only the artifact backend; the bookmark backend remains valid.
                if (BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true)
                    services.Remove(descriptor);
            }
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
}
