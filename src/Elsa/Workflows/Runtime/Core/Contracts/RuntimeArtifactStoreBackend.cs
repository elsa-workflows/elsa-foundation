using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks the exact registrations owned by the selected runtime-artifact backend.</summary>
/// <remarks>
/// Ownership is represented by the descriptor instances themselves. This deliberately avoids inferring ownership
/// from implementation namespaces, which would allow an unrelated host registration to be removed accidentally.
/// </remarks>
public sealed class RuntimeArtifactStoreBackend
{
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

        var ownedContractTypes = descriptors.Select(descriptor => descriptor.ServiceType).Distinct().ToArray();
        foreach (var contractType in ownedContractTypes)
        {
            var registrations = services.Where(descriptor => descriptor.ServiceType == contractType).ToArray();
            if (registrations.Any(registration => !descriptors.Contains(registration)))
                throw new InvalidOperationException($"Runtime artifact backend '{Name}' no longer exclusively owns {contractType.Name}.");
        }
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            removeOwnedArtifacts?.Invoke(services);
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
}
