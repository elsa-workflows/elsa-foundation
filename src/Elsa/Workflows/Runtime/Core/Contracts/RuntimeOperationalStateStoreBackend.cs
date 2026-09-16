using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the runtime operational-state provider family.</summary>
public sealed class RuntimeOperationalStateStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";

    private readonly IReadOnlyList<ServiceDescriptor> _descriptors;
    private readonly Action<IServiceCollection>? _remove;

    public RuntimeOperationalStateStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors, Action<IServiceCollection>? remove = null)
    {
        if (name is not (EntityFramework or InMemory))
            throw new ArgumentException($"Unknown runtime operational-state backend '{name}'.", nameof(name));
        _descriptors = descriptors?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(descriptors));
        if (_descriptors.Count == 0)
            throw new ArgumentException("At least one operational-state descriptor is required.", nameof(descriptors));
        Name = name;
        _remove = remove;
    }

    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => _descriptors.Contains(descriptor);
    public static RuntimeOperationalStateStoreBackend? Find(IServiceCollection services) => services.Select(x => x.ImplementationInstance).OfType<RuntimeOperationalStateStoreBackend>().SingleOrDefault();
    public static void Register(IServiceCollection services, RuntimeOperationalStateStoreBackend backend) => services.AddSingleton(backend);

    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        var unowned = services.Where(x =>
            IsOperationalContract(x.ServiceType) && !IsRuntimeCoreDefault(x));
        if (unowned.Any())
            throw new InvalidOperationException("An explicit runtime operational-state store registration is already present; the selected backend refuses to replace it implicitly.");
    }

    private static bool IsRuntimeCoreDefault(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is
            "Elsa.Workflows.Runtime.Core.Services.InMemoryDurableValueStateStore" or
            "Elsa.Workflows.Runtime.Core.Services.InMemorySchedulerStateStore" or
            "Elsa.Workflows.Runtime.Core.Services.InMemoryExecutionLivenessStateStore" or
            "Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowHoldStateStore" or
            "Elsa.Workflows.Runtime.Core.Services.InMemoryRuntimeRecoveryScanner" or
            "Elsa.Workflows.Runtime.Core.Services.InMemoryIncidentStateStore" ||
        descriptor.ImplementationFactory?.Method.DeclaringType?.FullName is { } declaringType &&
        (declaringType.Contains("RuntimeCoreServiceCollectionExtensions", StringComparison.Ordinal) ||
         declaringType.Contains("WorkflowsRuntimeAttentionFeature", StringComparison.Ordinal));

    private static bool IsOperationalContract(Type? serviceType) =>
        serviceType == typeof(IDurableValueStateStore) ||
        serviceType == typeof(ISchedulerStateStore) ||
        serviceType == typeof(IExecutionLivenessStateStore) ||
        serviceType == typeof(IWorkflowHoldStateStore) ||
        serviceType == typeof(IRuntimeRecoveryScanner) ||
        serviceType == typeof(IIncidentStateStore) ||
        serviceType?.FullName == "Elsa.Workflows.Runtime.Attention.IWorkflowRuntimeAttentionQuery";

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        var current = services.Where(x => IsOperationalContract(x.ServiceType)).ToArray();
        if (current.Any(x => !Owns(x)) || _descriptors.Any(x => !services.Contains(x)))
            throw new InvalidOperationException($"Runtime operational-state backend '{Name}' no longer exclusively owns its registrations.");
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services);
        foreach (var descriptor in _descriptors)
        {
            if (!IsOwnedBySibling(services, descriptor))
                services.Remove(descriptor);
        }
        for (var index = services.Count - 1; index >= 0; index--)
            if (ReferenceEquals(services[index].ImplementationInstance, this)) services.RemoveAt(index);
        return _remove;
    }

    private static bool IsOwnedBySibling(IServiceCollection services, ServiceDescriptor descriptor) =>
        RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) == true ||
        BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) == true ||
        RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) == true ||
        WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) == true;
}
