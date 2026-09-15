using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Records the selected owner of one Publishing persistence family: the contracts the family covers and the
/// exact service descriptors the selected backend registered for them.
/// </summary>
/// <remarks>
/// Ownership is recorded per family rather than inferred from implementation types, so switching backends
/// removes only what the previous owner added and a contract registered by anyone else is refused rather
/// than silently replaced. A backend may own no registration for some of the family's contracts (the
/// in-memory publication-receipt default has no commit commands, for example); every registration that does
/// exist for a family contract must then be one the backend owns.
/// </remarks>
public sealed class PublishingPersistenceFamilyBackend
{
    public const string InMemory = "in-memory";
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    /// <summary>P01: publication records.</summary>
    public const string PublicationRecords = "publication-records";

    /// <summary>P05: idempotent activity-publication receipts.</summary>
    public const string ActivityPublicationReceipts = "activity-publication-receipts";

    /// <summary>P06: activity draft test-run receipts and their expiry.</summary>
    public const string ActivityDraftTestRuns = "activity-draft-test-runs";

    /// <summary>
    /// The reusable-activity publication commit commands. A command writes the P05 receipt as part of its
    /// commit, so a backend that owns the receipts must own the commands as well.
    /// </summary>
    public const string ActivityPublicationCommands = "activity-publication-commands";

    private readonly IReadOnlyCollection<ServiceDescriptor> descriptors;

    public PublishingPersistenceFamilyBackend(
        string family,
        string name,
        IReadOnlyCollection<Type> contracts,
        IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (name is not (InMemory or Groundwork or EntityFramework))
            throw new ArgumentException($"Unknown Publishing persistence backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(descriptors);
        Contracts = contracts.Distinct().ToArray();
        if (Contracts.Count == 0)
            throw new ArgumentException("A Publishing persistence family must name at least one contract.", nameof(contracts));
        this.descriptors = descriptors.Distinct().ToArray();
        if (!this.descriptors.Any(descriptor => Contracts.Contains(descriptor.ServiceType)))
            throw new ArgumentException($"The descriptors owned for family '{family}' must register at least one of its contracts.", nameof(descriptors));
        Family = family;
        Name = name;
    }

    public string Family { get; }

    public string Name { get; }

    public IReadOnlyCollection<Type> Contracts { get; }

    public static IReadOnlyCollection<Type> PublicationRecordContracts { get; } = [typeof(IPublicationRecordStore)];

    public static IReadOnlyCollection<Type> ActivityPublicationReceiptContracts { get; } = [typeof(IActivityPublicationReceiptStore)];

    public static IReadOnlyCollection<Type> ActivityDraftTestRunContracts { get; } = [typeof(IActivityDraftTestRunStore)];

    public bool Owns(ServiceDescriptor candidate) => descriptors.Contains(candidate);

    /// <summary>The recorded owner of <paramref name="family"/>, or <c>null</c> when none was recorded.</summary>
    public static PublishingPersistenceFamilyBackend? Find(IServiceCollection services, string family)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return services
            .Select(service => service.ImplementationInstance)
            .OfType<PublishingPersistenceFamilyBackend>()
            .SingleOrDefault(backend => StringComparer.Ordinal.Equals(backend.Family, family));
    }

    public static void Register(IServiceCollection services, PublishingPersistenceFamilyBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        if (Find(services, backend.Family) is not null)
            throw new InvalidOperationException($"Publishing persistence family '{backend.Family}' already has a recorded owner.");
        services.AddSingleton(backend);
    }

    /// <summary>Records the descriptors a registration just added as the owner of <paramref name="family"/>.</summary>
    public static PublishingPersistenceFamilyBackend RegisterAdded(
        IServiceCollection services,
        string family,
        string name,
        IReadOnlyCollection<Type> contracts,
        int firstAddedIndex)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfNegative(firstAddedIndex);
        var backend = new PublishingPersistenceFamilyBackend(family, name, contracts, services.Skip(firstAddedIndex).ToArray());
        Register(services, backend);
        return backend;
    }

    /// <summary>
    /// Adds the in-memory default for <paramref name="family"/> and records it as the family's owner, but only
    /// when nothing registered the family before. A host-provided registration stays foreign, so a durable
    /// backend selected later fails closed instead of treating custom behavior as the in-memory default.
    /// </summary>
    public static void TryAddInMemory<TService, TImplementation>(
        IServiceCollection services,
        string family,
        IReadOnlyCollection<Type> contracts)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contracts);
        if (services.Any(service => contracts.Contains(service.ServiceType)) || Find(services, family) is not null)
            return;

        var firstAdded = services.Count;
        services.AddSingleton<TService, TImplementation>();
        RegisterAdded(services, family, InMemory, contracts, firstAdded);
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Publishing persistence backend '{Name}' no longer owns one of its '{Family}' registrations.");

        foreach (var contract in Contracts)
        {
            var registrations = services.Where(service => service.ServiceType == contract).ToArray();
            if (registrations.Length > 1 || registrations.Any(registration => !Owns(registration)))
                throw new InvalidOperationException($"Publishing persistence backend '{Name}' no longer exclusively owns its {contract.Name} registration.");
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

    public static void EnsureNoUnownedRegistrations(IServiceCollection services, string family, IReadOnlyCollection<Type> contracts)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contracts);
        var foreign = services.FirstOrDefault(service => contracts.Contains(service.ServiceType));
        if (foreign is not null)
            throw new InvalidOperationException(
                $"An explicit {foreign.ServiceType.Name} registration is already present for Publishing persistence family '{family}'; the selected backend refuses to replace it implicitly.");
    }

    /// <summary>
    /// Clears the way for <paramref name="selected"/> to register <paramref name="family"/>: returns <c>false</c>
    /// when that backend already owns it, removes another recorded owner's registrations, and refuses a
    /// registration no backend recorded.
    /// </summary>
    public static bool PrepareSelection(IServiceCollection services, string family, IReadOnlyCollection<Type> contracts, string selected)
    {
        var existing = Find(services, family);
        if (existing is null)
        {
            EnsureNoUnownedRegistrations(services, family, contracts);
            return true;
        }

        if (existing.Name == selected)
        {
            existing.EnsureOwnsRegisteredContracts(services);
            return false;
        }

        existing.RemoveOwnedArtifacts(services);
        EnsureNoUnownedRegistrations(services, family, contracts);
        return true;
    }
}
