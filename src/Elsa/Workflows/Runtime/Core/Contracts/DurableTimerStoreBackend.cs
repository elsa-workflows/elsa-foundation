using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the selected owner of the durable-timer contract and its EF context registrations.</summary>
/// <remarks>
/// The timer contract predates the provider ownership markers used by the other runtime stores. This marker keeps
/// opt-in replacement explicit while still allowing the Runtime in-memory default and the Groundwork bridge to be
/// replaced by an EF provider. Descriptors are tracked by identity so an unrelated host registration is never removed.
/// </remarks>
public sealed class DurableTimerStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string Groundwork = "groundwork";
    public const string InMemory = "in-memory";

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public DurableTimerStoreBackend(
        string name,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not (EntityFramework or Groundwork or InMemory))
            throw new ArgumentException($"Unknown durable-timer store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned durable-timer descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static DurableTimerStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<DurableTimerStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, DurableTimerStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureTimerSurfaceRegistrations(IServiceCollection services) =>
        services.Where(IsTimerSurfaceRegistration).ToArray();

    /// <summary>Rejects an explicit host timer registration that an opt-in provider cannot safely replace.</summary>
    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => IsTimerSurfaceRegistration(descriptor) && !IsRuntimeDefault(descriptor) && !IsGroundwork(descriptor)))
            throw new InvalidOperationException("An explicit durable-timer store registration is already present; the selected backend refuses to replace it implicitly.");
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Durable-timer backend '{Name}' no longer owns one of its registrations.");
        if (services.Any(descriptor => IsTimerSurfaceRegistration(descriptor) && !Owns(descriptor)))
            throw new InvalidOperationException($"Durable-timer backend '{Name}' no longer exclusively owns the IDurableTimerStore registration.");
    }

    /// <summary>Removes this backend's contract and returns auxiliary cleanup to invoke after replacement succeeds.</summary>
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            foreach (var descriptor in descriptors.Where(descriptor =>
                         !IsOwnedBySibling(services, descriptor)))
                services.Remove(descriptor);
            for (var index = services.Count - 1; index >= 0; index--)
                if (ReferenceEquals(services[index].ImplementationInstance, this))
                    services.RemoveAt(index);
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

    private static bool IsTimerSurfaceRegistration(ServiceDescriptor descriptor) =>
        typeof(IDurableTimerStore).IsAssignableFrom(descriptor.ServiceType) ||
        descriptor.ImplementationType is { } implementationType && typeof(IDurableTimerStore).IsAssignableFrom(implementationType) ||
        descriptor.ImplementationInstance is { } implementationInstance && implementationInstance is IDurableTimerStore ||
        descriptor.ImplementationFactory?.Method.ReturnType is { } returnType && typeof(IDurableTimerStore).IsAssignableFrom(returnType);

    private static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.Name == "InMemoryDurableTimerStore" ||
        descriptor.ImplementationFactory?.Method.DeclaringType?.FullName?.Contains("RuntimeCoreServiceCollectionExtensions", StringComparison.Ordinal) == true;

    private static bool IsGroundwork(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.Name == "GroundworkV2DurableTimerStateStore" ||
        descriptor.ImplementationFactory?.Method.ReturnType.Name == "GroundworkV2DurableTimerStateStore";

    private static bool IsOwnedBySibling(IServiceCollection services, ServiceDescriptor descriptor) =>
        RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) == true ||
        BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) == true;
}
