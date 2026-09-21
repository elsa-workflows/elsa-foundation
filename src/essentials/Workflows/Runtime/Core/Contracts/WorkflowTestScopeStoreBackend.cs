using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the test-scope lifecycle/admission/cleanup family.</summary>
public sealed class WorkflowTestScopeStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";
    private readonly IReadOnlyList<ServiceDescriptor> _descriptors;
    private readonly Action<IServiceCollection>? _remove;
    public WorkflowTestScopeStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors, Action<IServiceCollection>? remove = null) { if (name is not (EntityFramework or InMemory)) throw new ArgumentException($"Unknown test-scope backend '{name}'.", nameof(name)); _descriptors = descriptors?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(descriptors)); if (_descriptors.Count == 0) throw new ArgumentException("At least one test-scope descriptor is required.", nameof(descriptors)); _remove = remove; Name = name; }
    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => _descriptors.Contains(descriptor);
    public static WorkflowTestScopeStoreBackend? Find(IServiceCollection services) => services.Select(x => x.ImplementationInstance).OfType<WorkflowTestScopeStoreBackend>().SingleOrDefault();
    public static void Register(IServiceCollection services, WorkflowTestScopeStoreBackend backend) => services.AddSingleton(backend);
    public static void EnsureNoUnownedRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var contracts = services.Where(descriptor => descriptor.ServiceType is not null &&
                                                     (descriptor.ServiceType == typeof(IWorkflowTestScopeStore) ||
                                                      descriptor.ServiceType == typeof(IWorkflowTestScopeAdmissionStore) ||
                                                      descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore)));
        if (contracts.Any(descriptor => !IsCoreRegistration(descriptor)))
            throw new InvalidOperationException("An explicit workflow test-scope registration is already present; the selected backend refuses to replace it implicitly.");
    }
    public void EnsureOwnsRegisteredContracts(IServiceCollection services) { var contracts = services.Where(x => x.ServiceType is not null && (x.ServiceType == typeof(IWorkflowTestScopeStore) || x.ServiceType == typeof(IWorkflowTestScopeAdmissionStore) || x.ServiceType == typeof(IWorkflowTestScopeCleanupStore))).ToArray(); if (_descriptors.Any(x => !services.Contains(x)) || contracts.Any(x => !Owns(x))) throw new InvalidOperationException($"Test-scope backend '{Name}' no longer exclusively owns its registrations."); }
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services) { EnsureOwnsRegisteredContracts(services); foreach (var d in _descriptors.Where(x => RuntimeArtifactStoreBackend.Find(services)?.Owns(x) != true && RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(x) != true && BookmarkStateStoreBackend.Find(services)?.Owns(x) != true && WorkflowExecutionStateStoreBackend.Find(services)?.Owns(x) != true && RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(x) != true && RuntimeOperationalStateStoreBackend.Find(services)?.Owns(x) != true)) services.Remove(d); for (var i = services.Count - 1; i >= 0; i--) if (ReferenceEquals(services[i].ImplementationInstance, this)) services.RemoveAt(i); return _remove; }
    private static bool IsCoreRegistration(ServiceDescriptor descriptor) =>
        RuntimeCoreRegistrationOwnership.IsCoreFactory(descriptor);
}
