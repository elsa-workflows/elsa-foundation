using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks the selected transaction-owning Runtime checkpoint contract.</summary>
public sealed class RuntimeCheckpointCommitStoreBackend
{
    public const string EntityFramework = "entity-framework";
    public const string Groundwork = "groundwork";
    public const string InMemory = "in-memory";

    private readonly ServiceDescriptor _contract;
    private readonly ServiceDescriptor _concrete;
    private readonly ServiceDescriptor? _durabilityEvidence;

    public RuntimeCheckpointCommitStoreBackend(string name, ServiceDescriptor contract, ServiceDescriptor concrete, ServiceDescriptor? durabilityEvidence = null)
    {
        if (name is not (EntityFramework or Groundwork or InMemory))
            throw new ArgumentException($"Unknown Runtime checkpoint backend '{name}'.", nameof(name));
        if (contract.ServiceType != typeof(IRuntimeCheckpointCommitStore))
            throw new ArgumentException("The selected checkpoint descriptor must own its public contract.", nameof(contract));
        _contract = contract;
        _concrete = concrete ?? throw new ArgumentNullException(nameof(concrete));
        if (durabilityEvidence is not null && durabilityEvidence.ServiceType != typeof(IWorkflowDispatchDurabilityEvidence))
            throw new ArgumentException("Checkpoint durability evidence must implement its public evidence contract.", nameof(durabilityEvidence));
        _durabilityEvidence = durabilityEvidence;
        Name = name;
    }

    public string Name { get; }

    public static RuntimeCheckpointCommitStoreBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<RuntimeCheckpointCommitStoreBackend>()
        .SingleOrDefault();

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        var contracts = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore)).ToArray();
        if (contracts.Length != 1 || !ReferenceEquals(contracts[0], _contract) ||
            !services.Contains(_concrete) || _durabilityEvidence is not null && !services.Contains(_durabilityEvidence))
            throw new InvalidOperationException($"Runtime checkpoint backend '{Name}' no longer exclusively owns its registrations.");
    }

    public void RemoveOwnedRegistrations(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        services.Remove(_contract);
        services.Remove(_concrete);
        if (_durabilityEvidence is not null)
            services.Remove(_durabilityEvidence);
        services.Remove(services.Single(descriptor => ReferenceEquals(descriptor.ImplementationInstance, this)));
    }

    public static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore) &&
        RuntimeCoreRegistrationOwnership.IsCoreFactory(descriptor);

    public static void Register(IServiceCollection services, RuntimeCheckpointCommitStoreBackend backend) =>
        services.AddSingleton(backend);
}
