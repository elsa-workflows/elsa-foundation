using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the complete alteration plan/job provider family.</summary>
public sealed class RuntimeWorkflowAlterationStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";
    private readonly IReadOnlyList<ServiceDescriptor> _descriptors;
    private readonly Action<IServiceCollection>? _remove;
    public RuntimeWorkflowAlterationStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors, Action<IServiceCollection>? remove = null)
    {
        if (name is not (EntityFramework or InMemory)) throw new ArgumentException($"Unknown alteration backend '{name}'.", nameof(name));
        _descriptors = descriptors?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(descriptors));
        if (_descriptors.Count == 0) throw new ArgumentException("At least one alteration descriptor is required.", nameof(descriptors));
        _remove = remove; Name = name;
    }
    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => _descriptors.Contains(descriptor);
    public static RuntimeWorkflowAlterationStoreBackend? Find(IServiceCollection services) => services.Select(x => x.ImplementationInstance).OfType<RuntimeWorkflowAlterationStoreBackend>().SingleOrDefault();
    public static void Register(IServiceCollection services, RuntimeWorkflowAlterationStoreBackend backend) => services.AddSingleton(backend);
    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IWorkflowAlterationStore) &&
                                       descriptor.ImplementationType?.FullName != "Elsa.Workflows.Runtime.Services.Alterations.InMemoryWorkflowAlterationStore"))
            throw new InvalidOperationException("An explicit workflow alteration store registration is already present; the selected backend refuses to replace it implicitly.");
    }
    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        var current = services.Where(x => x.ServiceType == typeof(IWorkflowAlterationStore)).ToArray();
        if (current.Length != 1 || !Owns(current[0]) || _descriptors.Any(x => !services.Contains(x))) throw new InvalidOperationException($"Alteration backend '{Name}' no longer exclusively owns its registrations.");
    }
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContracts(services); foreach (var d in _descriptors.Where(x => RuntimeArtifactStoreBackend.Find(services)?.Owns(x) != true && RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(x) != true && BookmarkStateStoreBackend.Find(services)?.Owns(x) != true && WorkflowExecutionStateStoreBackend.Find(services)?.Owns(x) != true && WorkflowTestScopeStoreBackend.Find(services)?.Owns(x) != true && RuntimeOperationalStateStoreBackend.Find(services)?.Owns(x) != true)) services.Remove(d); for (var i = services.Count - 1; i >= 0; i--) if (ReferenceEquals(services[i].ImplementationInstance, this)) services.RemoveAt(i); return _remove;
    }
}
