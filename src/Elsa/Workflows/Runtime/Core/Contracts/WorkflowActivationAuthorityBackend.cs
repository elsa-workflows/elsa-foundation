using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks ownership of the Runtime workflow activation-authority contract.</summary>
public sealed class WorkflowActivationAuthorityBackend
{
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";

    private readonly ServiceDescriptor contract;
    private readonly ServiceDescriptor concrete;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public WorkflowActivationAuthorityBackend(
        string name,
        ServiceDescriptor contract,
        ServiceDescriptor concrete,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        if (name is not (EntityFramework or InMemory))
            throw new ArgumentException($"Unknown workflow activation-authority backend '{name}'.", nameof(name));
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(concrete);
        if (contract.ServiceType != typeof(IWorkflowActivationAuthority))
            throw new ArgumentException("The activation-authority contract descriptor is invalid.", nameof(contract));

        Name = name;
        this.contract = contract;
        this.concrete = concrete;
        this.removeOwnedArtifacts = removeOwnedArtifacts;
    }

    public string Name { get; }

    public bool Owns(ServiceDescriptor descriptor) =>
        ReferenceEquals(descriptor, contract) || ReferenceEquals(descriptor, concrete);

    public static WorkflowActivationAuthorityBackend? Find(IServiceCollection services) => services
        .Select(x => x.ImplementationInstance)
        .OfType<WorkflowActivationAuthorityBackend>()
        .SingleOrDefault();

    public static void Register(IServiceCollection services, WorkflowActivationAuthorityBackend backend) => services.AddSingleton(backend);

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        var current = services.Where(x => x.ServiceType == typeof(IWorkflowActivationAuthority)).ToArray();
        var concrete = services.Where(x => x.ServiceType == this.concrete.ServiceType).ToArray();
        if (current.Length != 1 || !ReferenceEquals(current[0], contract) ||
            concrete.Length != 1 || !ReferenceEquals(concrete[0], this.concrete))
            throw new InvalidOperationException($"Workflow activation-authority backend '{Name}' no longer exclusively owns its registrations.");
    }

    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        services.Remove(contract);
        services.Remove(concrete);
        var marker = services.Single(x => ReferenceEquals(x.ImplementationInstance, this));
        services.Remove(marker);
        return removeOwnedArtifacts;
    }
}
