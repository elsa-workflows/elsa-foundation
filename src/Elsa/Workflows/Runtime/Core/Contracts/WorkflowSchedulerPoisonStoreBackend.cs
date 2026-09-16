using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the scheduler-poison store and any EF context descriptors it shares.</summary>
/// <remarks>
/// Scheduler poison is deliberately kept outside <see cref="RuntimeOperationalStateStoreBackend"/>. It is a
/// separate contract with its own opt-in transition so adding the EF adapter cannot accidentally replace the
/// durable operational-state family or change checkpoint ownership.
/// </remarks>
public sealed class WorkflowSchedulerPoisonStoreBackend
{
    private static readonly Type[] ContractTypes = [typeof(IWorkflowSchedulerPoisonStore)];
    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";

    public WorkflowSchedulerPoisonStoreBackend(
        string name,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not (EntityFramework or InMemory))
            throw new ArgumentException($"Unknown scheduler-poison store backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one owned scheduler-poison descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static WorkflowSchedulerPoisonStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<WorkflowSchedulerPoisonStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, WorkflowSchedulerPoisonStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public static IReadOnlyCollection<ServiceDescriptor> CaptureSurfaceRegistrations(IServiceCollection services) =>
        services.Where(IsSurfaceRegistration).ToArray();

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => IsSurfaceRegistration(descriptor) && !IsKnownRuntimeRegistration(descriptor)))
            throw new InvalidOperationException("An explicit scheduler-poison store registration is already present; the selected backend refuses to replace it implicitly.");
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Scheduler-poison backend '{Name}' no longer owns one of its registrations.");
        if (services.Any(descriptor => IsSurfaceRegistration(descriptor) && !Owns(descriptor)))
            throw new InvalidOperationException($"Scheduler-poison backend '{Name}' no longer exclusively owns the IWorkflowSchedulerPoisonStore registration.");
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

    private static bool IsSurfaceRegistration(ServiceDescriptor descriptor) => ContractTypes.Any(contract =>
        contract.IsAssignableFrom(descriptor.ServiceType) ||
        descriptor.ImplementationType is { } implementationType && contract.IsAssignableFrom(implementationType) ||
        descriptor.ImplementationInstance is { } implementationInstance && contract.IsAssignableFrom(implementationInstance.GetType()) ||
        descriptor.ImplementationFactory?.Method.ReturnType is { } returnType && contract.IsAssignableFrom(returnType));

    private static bool IsKnownRuntimeRegistration(ServiceDescriptor descriptor)
    {
        if (!IsSurfaceRegistration(descriptor))
            return true;

        var implementationType = descriptor.ImplementationType ??
                                 descriptor.ImplementationInstance?.GetType() ??
                                 descriptor.ImplementationFactory?.Method.ReturnType;
        if (implementationType is not null && implementationType.Name is "InMemoryWorkflowSchedulerPoisonStore")
            return true;

        return descriptor.ImplementationFactory?.Method.DeclaringType?.FullName?.Contains(
            "RuntimeCoreServiceCollectionExtensions", StringComparison.Ordinal) == true;
    }

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
