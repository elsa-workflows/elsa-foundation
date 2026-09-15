using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Tracks the selected owner of workflow execution state persistence.</summary>
public sealed class WorkflowExecutionStateStoreBackend
{
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";
    public const string InMemory = "in-memory";
    private readonly IReadOnlyList<ServiceDescriptor> descriptors;
    private readonly Action<IServiceCollection>? removeOwnedArtifacts;

    public WorkflowExecutionStateStoreBackend(string name, IEnumerable<ServiceDescriptor> descriptors, Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        if (name is not Groundwork and not EntityFramework and not InMemory) throw new ArgumentException($"Unknown workflow execution state store backend '{name}'.", nameof(name));
        this.descriptors = descriptors?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(descriptors));
        if (this.descriptors.Count == 0) throw new ArgumentException("At least one owned workflow execution state descriptor is required.", nameof(descriptors));
        this.removeOwnedArtifacts = removeOwnedArtifacts;
        Name = name;
    }
    public string Name { get; }
    public bool Owns(ServiceDescriptor descriptor) => descriptors.Contains(descriptor);
    public static WorkflowExecutionStateStoreBackend? Find(IServiceCollection services) => services.Select(x => x.ImplementationInstance).OfType<WorkflowExecutionStateStoreBackend>().SingleOrDefault();
    public static void Register(IServiceCollection services, WorkflowExecutionStateStoreBackend backend) => services.AddSingleton(backend);
    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        var current = services.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)).ToArray();
        if (current.Length != 1 || !descriptors.Contains(current[0])) throw new InvalidOperationException($"Workflow execution state backend '{Name}' no longer exclusively owns IWorkflowExecutionStateStore.");
        if (descriptors.Any(x => !services.Contains(x))) throw new InvalidOperationException($"Workflow execution state backend '{Name}' no longer owns one of its registrations.");
    }
    public Action<IServiceCollection>? PrepareRemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        foreach (var descriptor in descriptors.Where(descriptor =>
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
        for (var i = services.Count - 1; i >= 0; i--) if (ReferenceEquals(services[i].ImplementationInstance, this)) services.RemoveAt(i);
        return removeOwnedArtifacts;
    }
}
