using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Composition;

/// <summary>
/// Records the exact service descriptors owned by the selected Elsa 3 import persistence backend, so a
/// host can repeat a registration idempotently and never
/// silently replace a custom import store or command.
/// </summary>
public sealed class Elsa3ImportPersistenceBackend
{
    public const string EntityFramework = "entity-framework";

    /// <summary>
    /// Contracts an import persistence backend replaces. A registration of one of these types must be
    /// owned by the selected backend or is rejected before any mutation.
    /// </summary>
    public static IReadOnlySet<Type> ReplacementContractTypes { get; } = new HashSet<Type>
    {
        typeof(IReusableActivityImportOperationStore),
        typeof(IReusableActivityImportCommand)
    };

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? withdrawExternalDeclarations;

    /// <param name="withdrawExternalDeclarations">
    /// Withdraws declarations the backend keeps outside its owned descriptors, such as storage units in a
    /// shared catalog, when another backend replaces it.
    /// </param>
    public Elsa3ImportPersistenceBackend(
        string name,
        string configurationFingerprint,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? withdrawExternalDeclarations = null)
    {
        if (name is not EntityFramework)
            throw new ArgumentException($"Unknown Elsa 3 import persistence backend '{name}'.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFingerprint);
        ArgumentNullException.ThrowIfNull(descriptors);

        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned Elsa 3 import persistence descriptor is required.", nameof(descriptors));

        Name = name;
        ConfigurationFingerprint = configurationFingerprint;
        this.withdrawExternalDeclarations = withdrawExternalDeclarations;
    }

    public string Name { get; }

    public string ConfigurationFingerprint { get; }

    public IReadOnlyList<ServiceDescriptor> Descriptors => descriptors;

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public bool HasConfiguration(string name, string configurationFingerprint) =>
        StringComparer.Ordinal.Equals(Name, name) &&
        StringComparer.Ordinal.Equals(ConfigurationFingerprint, configurationFingerprint);

    /// <summary>True when an import replacement contract is registered outside any recorded backend.</summary>
    public static bool HasUnownedReplacementContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var backend = Find(services);
        return services.Any(descriptor =>
            ReplacementContractTypes.Contains(descriptor.ServiceType) &&
            (backend is null || !backend.Owns(descriptor)));
    }

    public static Elsa3ImportPersistenceBackend? Find(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<Elsa3ImportPersistenceBackend>()
            .SingleOrDefault();
    }

    public static void Register(IServiceCollection services, Elsa3ImportPersistenceBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    /// <summary>Creates a collision-safe ordinal fingerprint for backend configuration fields.</summary>
    public static string Fingerprint(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join("|", values.Select(value => value is null ? "-1:" : $"{value.Length}:{value}"));
    }

    /// <summary>
    /// Confirms every owned descriptor is still registered and that no other registration shares a
    /// replacement contract with it. Shared host infrastructure the backend adds stays additive.
    /// </summary>
    public void EnsureOwnsRegisteredDescriptors(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Elsa 3 import backend '{Name}' no longer owns one of its registrations.");

        foreach (var serviceType in ReplacementContractTypes)
        {
            if (services.Any(descriptor => descriptor.ServiceType == serviceType && !Owns(descriptor)))
                throw new InvalidOperationException($"Elsa 3 import backend '{Name}' no longer exclusively owns {serviceType.Name}.");
        }
    }

    /// <summary>
    /// Removes this backend's exact descriptors and its ownership marker, then withdraws its external
    /// declarations. Callers that must stay atomic snapshot that external state before calling.
    /// </summary>
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
            withdrawExternalDeclarations?.Invoke(services);
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
