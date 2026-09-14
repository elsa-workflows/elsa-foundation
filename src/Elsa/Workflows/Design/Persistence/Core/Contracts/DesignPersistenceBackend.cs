using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>Tracks the exact service descriptors owned by the selected design persistence backend.</summary>
public sealed class DesignPersistenceBackend
{
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;

    public DesignPersistenceBackend(string name, IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not Groundwork and not EntityFramework)
            throw new ArgumentException($"Unknown design persistence backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned design-persistence descriptor is required.", nameof(descriptors));
        Name = name;
    }

    public string Name { get; }

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

    private void EnsureOwnsRegisteredDescriptors(IServiceCollection services)
    {
        foreach (var descriptor in descriptors)
        {
            if (!services.Contains(descriptor))
                throw new InvalidOperationException($"Design persistence backend '{Name}' no longer owns one of its registrations.");
        }

        foreach (var serviceType in descriptors.Select(descriptor => descriptor.ServiceType).Distinct())
        {
            var registrations = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
            if (registrations.Any(registration => !descriptors.Contains(registration)))
                throw new InvalidOperationException($"Design persistence backend '{Name}' no longer exclusively owns {serviceType.Name}.");
        }
    }
}
