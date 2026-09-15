using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks the selected owner of the recurring-start schedule store contract.</summary>
public sealed class RecurringTriggerScheduleStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string Groundwork = "groundwork";
    public const string InMemory = "in-memory";

    private readonly ServiceDescriptor _contract;
    private readonly ServiceDescriptor _concrete;
    private readonly Action<IServiceCollection>? _removeOwnedArtifacts;

    public RecurringTriggerScheduleStoreBackend(
        string name,
        ServiceDescriptor contract,
        ServiceDescriptor concrete,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        if (name is not (EntityFramework or Groundwork or InMemory))
            throw new ArgumentException($"Unknown recurring-trigger schedule backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(concrete);
        if (contract.ServiceType != typeof(IRecurringTriggerScheduleStore))
            throw new ArgumentException("The recurring-trigger schedule contract descriptor is invalid.", nameof(contract));
        _contract = contract;
        _concrete = concrete;
        _removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => ReferenceEquals(descriptor, _contract) || ReferenceEquals(descriptor, _concrete);

    public static RecurringTriggerScheduleStoreBackend? Find(IServiceCollection services) =>
        services.Select(x => x.ImplementationInstance).OfType<RecurringTriggerScheduleStoreBackend>().SingleOrDefault();

    public static void Register(IServiceCollection services, RecurringTriggerScheduleStoreBackend backend) => services.AddSingleton(backend);

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var contracts = services.Where(x => x.ServiceType == typeof(IRecurringTriggerScheduleStore)).ToArray();
        var concretes = services.Where(x => x.ServiceType == _concrete.ServiceType).ToArray();
        if (contracts.Length != 1 || !ReferenceEquals(contracts[0], _contract) || concretes.Length != 1 || !ReferenceEquals(concretes[0], _concrete))
            throw new InvalidOperationException($"Recurring-trigger schedule backend '{Name}' no longer exclusively owns its registrations.");
    }

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => IsSurfaceRegistration(descriptor) && !IsRuntimeDefault(descriptor)))
            throw new InvalidOperationException("An explicit recurring-trigger schedule store registration is already present; EF persistence refuses to replace it implicitly.");
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureDefaultRegistrations(IServiceCollection services) =>
        services.Where(descriptor => IsSurfaceRegistration(descriptor) && IsRuntimeDefault(descriptor)).ToArray();

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        var snapshot = services.ToArray();
        try
        {
            services.Remove(_contract);
            services.Remove(_concrete);
            var marker = services.Single(x => ReferenceEquals(x.ImplementationInstance, this));
            services.Remove(marker);
            return _removeOwnedArtifacts;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static bool IsSurfaceRegistration(ServiceDescriptor descriptor) =>
        typeof(IRecurringTriggerScheduleStore).IsAssignableFrom(descriptor.ServiceType) ||
        descriptor.ImplementationType is { } implementationType && typeof(IRecurringTriggerScheduleStore).IsAssignableFrom(implementationType) ||
        descriptor.ImplementationInstance is IRecurringTriggerScheduleStore ||
        descriptor.ImplementationFactory?.Method.ReturnType is { } returnType && typeof(IRecurringTriggerScheduleStore).IsAssignableFrom(returnType);

    private static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { } implementationType &&
        implementationType.FullName == "Elsa.Workflows.Runtime.Core.Services.InMemoryRecurringTriggerScheduleStore" &&
        implementationType.Assembly.GetName().Name == "Elsa.Workflows.Runtime" ||
        RuntimeCoreRegistrationOwnership.IsCoreFactory(descriptor);
}
