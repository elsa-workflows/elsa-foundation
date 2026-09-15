using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the selected owner of the scheduler-work queue contracts and their EF context registrations.</summary>
public sealed class SchedulerWorkQueueStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string Groundwork = "groundwork";
    public const string InMemory = "in-memory";

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public SchedulerWorkQueueStoreBackend(
        string name,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not (EntityFramework or Groundwork or InMemory))
            throw new ArgumentException($"Unknown scheduler-work queue backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned scheduler-work descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static SchedulerWorkQueueStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<SchedulerWorkQueueStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, SchedulerWorkQueueStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureQueueSurfaceRegistrations(IServiceCollection services) =>
        services.Where(IsQueueSurfaceRegistration).ToArray();

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => IsQueueSurfaceRegistration(descriptor) && !IsRuntimeDefault(descriptor)))
            throw new InvalidOperationException("An explicit scheduler-work queue registration is already present; the selected backend refuses to replace it implicitly.");
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Scheduler-work queue backend '{Name}' no longer owns one of its registrations.");
        if (services.Any(descriptor => IsQueueSurfaceRegistration(descriptor) && !Owns(descriptor)))
            throw new InvalidOperationException($"Scheduler-work queue backend '{Name}' no longer exclusively owns its queue registrations.");
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            foreach (var descriptor in descriptors.Where(descriptor => !IsOwnedBySibling(services, descriptor)))
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

    private static bool IsQueueSurfaceRegistration(ServiceDescriptor descriptor) =>
        typeof(IWorkflowSchedulerWorkQueue).IsAssignableFrom(descriptor.ServiceType) ||
        typeof(IWorkflowSchedulerWorkClaimInspection).IsAssignableFrom(descriptor.ServiceType) ||
        descriptor.ImplementationType is { } implementationType &&
        (typeof(IWorkflowSchedulerWorkQueue).IsAssignableFrom(implementationType) || typeof(IWorkflowSchedulerWorkClaimInspection).IsAssignableFrom(implementationType)) ||
        descriptor.ImplementationInstance is { } implementationInstance &&
        (implementationInstance is IWorkflowSchedulerWorkQueue || implementationInstance is IWorkflowSchedulerWorkClaimInspection) ||
        descriptor.ImplementationFactory?.Method.ReturnType is { } returnType &&
        (typeof(IWorkflowSchedulerWorkQueue).IsAssignableFrom(returnType) || typeof(IWorkflowSchedulerWorkClaimInspection).IsAssignableFrom(returnType));

    private static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { } implementationType &&
        implementationType.FullName == "Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowSchedulerWorkQueue" &&
        implementationType.Assembly.GetName().Name == "Elsa.Workflows.Runtime" ||
        RuntimeCoreRegistrationOwnership.IsCoreFactory(descriptor);

    private static bool IsOwnedBySibling(IServiceCollection services, ServiceDescriptor descriptor) =>
        RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) == true ||
        BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        DurableTimerStoreBackend.Find(services)?.Owns(descriptor) == true;
}
