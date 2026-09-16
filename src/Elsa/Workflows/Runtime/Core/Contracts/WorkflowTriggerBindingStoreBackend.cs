using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the workflow trigger-binding store contract.</summary>
public sealed class WorkflowTriggerBindingStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";
    private readonly ServiceDescriptor _contract;
    private readonly ServiceDescriptor _concrete;
    private readonly Action<IServiceCollection>? _removeOwnedArtifacts;

    public WorkflowTriggerBindingStoreBackend(string name, ServiceDescriptor contract, ServiceDescriptor concrete, Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        if (name is not (EntityFramework or InMemory)) throw new ArgumentException($"Unknown trigger-binding backend '{name}'.", nameof(name));
        if (contract.ServiceType != typeof(IWorkflowTriggerBindingStore)) throw new ArgumentException("The trigger-binding contract descriptor is invalid.", nameof(contract));
        _contract = contract;
        _concrete = concrete;
        _removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }

    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => ReferenceEquals(descriptor, _contract) || ReferenceEquals(descriptor, _concrete);

    public static WorkflowTriggerBindingStoreBackend? Find(IServiceCollection services) =>
        services.Select(x => x.ImplementationInstance).OfType<WorkflowTriggerBindingStoreBackend>().SingleOrDefault();

    public static void Register(IServiceCollection services, WorkflowTriggerBindingStoreBackend backend) => services.AddSingleton(backend);

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        var current = services.Where(x => x.ServiceType == typeof(IWorkflowTriggerBindingStore)).ToArray();
        var concrete = services.Where(x => x.ServiceType == _concrete.ServiceType).ToArray();
        if (current.Length != 1 || !ReferenceEquals(current[0], _contract) ||
            concrete.Length != 1 || !ReferenceEquals(concrete[0], _concrete))
            throw new InvalidOperationException($"Workflow trigger-binding backend '{Name}' no longer exclusively owns its registrations.");
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        services.Remove(_contract);
        services.Remove(_concrete);
        var marker = services.Single(x => ReferenceEquals(x.ImplementationInstance, this));
        services.Remove(marker);
        return _removeOwnedArtifacts;
    }
}
