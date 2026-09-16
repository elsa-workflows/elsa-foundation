using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the workflow-dispatch contract family.</summary>
public sealed class RuntimeWorkflowDispatchStoreBackend
{
    private static readonly Type[] ContractTypes =
    [
        typeof(IWorkflowDispatchStore),
        typeof(IWorkflowDispatchQueryStore),
        typeof(IWorkflowDispatchDeleteStore),
        typeof(IWorkflowDispatchRetentionRootStore),
        typeof(IWorkflowDispatchAdmissionStore),
        typeof(IWorkflowDispatchCancellationStore)
    ];

    private readonly IReadOnlyList<ServiceDescriptor> descriptors;

    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";

    public RuntimeWorkflowDispatchStoreBackend(
        string name,
        IEnumerable<ServiceDescriptor> descriptors,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not (EntityFramework or InMemory))
            throw new ArgumentException($"Unknown workflow-dispatch backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors.Distinct().ToArray();
        if (this.descriptors.Count == 0)
            throw new ArgumentException("At least one workflow-dispatch descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);

    public static RuntimeWorkflowDispatchStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<RuntimeWorkflowDispatchStoreBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, RuntimeWorkflowDispatchStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var contractType in ContractTypes)
        {
            var registrations = services.Where(descriptor => descriptor.ServiceType == contractType).ToArray();
            if (registrations.Length != 1 || !Owns(registrations[0]))
                throw new InvalidOperationException($"Workflow-dispatch backend '{Name}' no longer exclusively owns {contractType.Name}.");
        }

        if (descriptors.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException($"Workflow-dispatch backend '{Name}' no longer owns one of its registrations.");
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        var snapshot = services.ToArray();
        try
        {
            foreach (var descriptor in descriptors)
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

    public static IReadOnlyCollection<ServiceDescriptor> CaptureContractRegistrations(IServiceCollection services) => services
        .Where(descriptor => ContractTypes.Contains(descriptor.ServiceType))
        .ToArray();

    public static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IWorkflowDispatchStore) &&
        descriptor.ImplementationType?.FullName == "Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowDispatchStore" ||
        ContractTypes.Contains(descriptor.ServiceType) &&
        RuntimeCoreRegistrationOwnership.IsCoreFactory(descriptor);

    public static void EnsureRuntimeDefaultsOwnRegisteredContracts(
        IServiceCollection services,
        IReadOnlyCollection<ServiceDescriptor> registrations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);
        if (registrations.Count == 0)
            return;
        if (registrations.Count != ContractTypes.Length || registrations.Any(descriptor => !IsRuntimeDefault(descriptor)))
            throw new InvalidOperationException("An explicit workflow-dispatch store registration is already present; EF persistence refuses to replace it implicitly.");
    }
}
