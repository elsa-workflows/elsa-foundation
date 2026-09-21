using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.Core.Composition;

/// <summary>
/// Records the exact service descriptors owned by the selected Activities Design persistence
/// backend. The record is deliberately local to Activities Design: workflow design and publishing
/// persistence have separate replacement contracts and must not accidentally switch this lane.
/// </summary>
public sealed class ActivitiesDesignPersistenceBackend
{
    public const string EntityFramework = "entity-framework";

    /// <summary>
    /// Contracts that an Activities Design persistence implementation replaces. A service with one
    /// of these types must either be owned by the selected backend or be rejected before mutation.
    /// The atomic writer interfaces live in the provider assemblies, so they are identified by the
    /// replacement-contract attribute in the registration extensions.
    /// </summary>
    public static IReadOnlySet<Type> ReplacementContractTypes { get; } = new HashSet<Type>
    {
        typeof(IActivityDefinitionStore),
        typeof(IActivityDefinitionVersionStore),
        typeof(IActivityAvailabilitySettingsStore),
        typeof(IActivityDefinitionManagementProjectionStore),
        typeof(IActivityDefinitionAuthoringStore),
        typeof(IActivityDefinitionDraftStore),
        typeof(IActivityDefinitionVersionPublicationStore),
        typeof(IActivityDefinitionLayoutStore),
        typeof(IActivityDraftValidationStore),
        typeof(IActivityForkStore),
        typeof(IActivityDirectDependencyStore),
        typeof(IActivityDependencyProjectionStore),
        typeof(IActivityUpgradePlanStore),
        typeof(IActivityUpgradeApplyReceiptStore),
        typeof(IActivityDependencyProjectionRebuilder),
        typeof(IAddActivityDefinitionCommand),
        typeof(IAddActivityDefinitionVersionCommand),
        typeof(ICreateActivityDefinitionCommand),
        typeof(ICreateActivityDraftCommand),
        typeof(IUpdateActivityDefinitionPresentationCommand),
        typeof(IUpdateActivityDraftPresentationCommand),
        typeof(IStoreActivityDraftValidationCommand),
        typeof(IChangeActivityVersionLifecycleCommand),
        typeof(ISetActivityDefinitionRecommendationCommand),
        typeof(ISaveActivityForkCandidateCommand),
        typeof(IPruneActivityForkCandidatesCommand),
        typeof(IApplyActivityForkCandidateCommand),
        typeof(ICreateActivityDraftConflictCopyCommand),
        typeof(IReplaceActivityDraftCommand),
        typeof(IApplyActivityContractProposalCommand),
        typeof(IDiscardActivityDraftCommand),
        typeof(IRecommendedActivityDefinitionPickerStore),
        typeof(IActivityDefinitionLookup),
        typeof(IActivityDefinitionHasher),
        typeof(IActivityDefinitionFactory),
        typeof(IActivityDefinitionVersionFactory)
    };

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? withdrawExternalDeclarations;

    /// <param name="withdrawExternalDeclarations">
    /// Withdraws declarations this backend keeps outside its owned descriptors, such as storage units in a
    /// shared catalog, when another backend replaces it. It lets a provider-neutral switch clean up after a
    /// backend without referencing that backend's assembly.
    /// </param>
    public ActivitiesDesignPersistenceBackend(
        string name,
        string configurationFingerprint,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? withdrawExternalDeclarations = null)
    {
        if (name is not EntityFramework)
            throw new ArgumentException($"Unknown Activities Design persistence backend '{name}'.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFingerprint);
        ArgumentNullException.ThrowIfNull(descriptors);

        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned Activities Design persistence descriptor is required.", nameof(descriptors));

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

    /// <summary>
    /// True for a replacement-contract registration that the framework supplies as a stand-in until a
    /// persistence backend is selected. Such a default is replaced, not rejected; anything else under a
    /// replacement contract is a deliberate host choice that a backend must never override implicitly.
    /// </summary>
    public static bool IsFrameworkDefault(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IActivityAvailabilitySettingsStore) &&
        descriptor.ImplementationType == typeof(InMemoryActivityAvailabilitySettingsStore);

    /// <summary>True when a replacement contract is registered by something other than a framework default.</summary>
    public static bool HasUnownedReplacementContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor => ReplacementContractTypes.Contains(descriptor.ServiceType) && !IsFrameworkDefault(descriptor));
    }

    /// <summary>
    /// Removes framework defaults ahead of a backend registration. Callers do this before recording where
    /// their owned descriptors begin, because removing an earlier descriptor shifts that position.
    /// </summary>
    public static void RemoveFrameworkDefaults(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services.Where(IsFrameworkDefault).ToArray())
            services.Remove(descriptor);
    }

    public static ActivitiesDesignPersistenceBackend? Find(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ActivitiesDesignPersistenceBackend>()
            .SingleOrDefault();
    }

    public static void Register(IServiceCollection services, ActivitiesDesignPersistenceBackend backend)
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
    /// Confirms that every descriptor captured by this backend is still present and remains the
    /// exclusive registration for its service type. A caller cannot append a second store or
    /// silently replace one of the backend's ports and then switch persistence families.
    /// </summary>
    public void EnsureOwnsRegisteredDescriptors(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in descriptors)
        {
            if (!services.Contains(descriptor))
                throw new InvalidOperationException($"Activities Design backend '{Name}' no longer owns one of its registrations.");
        }

        foreach (var serviceType in descriptors.Select(descriptor => descriptor.ServiceType).Distinct())
        {
            var owned = descriptors.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
            var registered = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
            if (registered.Length != owned.Length || owned.Any(descriptor => !registered.Contains(descriptor)))
                throw new InvalidOperationException($"Activities Design backend '{Name}' no longer exclusively owns {serviceType.Name}.");
        }
    }

    /// <summary>
    /// Removes this backend's exact descriptors, including its ownership marker, and withdraws its external
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
