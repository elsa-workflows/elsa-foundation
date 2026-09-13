using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Records the placement-store owner selected for a distributed runtime composition.
/// </summary>
/// <remarks>
/// The distributed feature has a process-local default, while persistence leaves may replace
/// only placement and leave the command transport untouched. Keeping this marker in the leaf's
/// contract assembly makes that composition order-independent without making Runtime.Core aware
/// of any persistence implementation.
/// </remarks>
public sealed class ExecutionPlacementStoreBackend
{
    private readonly ServiceDescriptor _placementDescriptor;
    private readonly Action<IServiceCollection>? _removeOwnedArtifacts;

    public const string InMemory = "in-memory";
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    public ExecutionPlacementStoreBackend(
        string name,
        ServiceDescriptor placementDescriptor,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        EnsureKnown(name);
        ArgumentNullException.ThrowIfNull(placementDescriptor);
        if (placementDescriptor.ServiceType != typeof(IExecutionPlacementStore))
            throw new ArgumentException("The owned descriptor must register IExecutionPlacementStore.", nameof(placementDescriptor));

        Name = name;
        _placementDescriptor = placementDescriptor;
        _removeOwnedArtifacts = removeOwnedArtifacts;
    }

    public string Name { get; }

    public static ExecutionPlacementStoreBackend? Find(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ExecutionPlacementStoreBackend>()
            .SingleOrDefault();
    }

    public static bool HasRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
    }

    public static void EnsureKnown(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not InMemory and not Groundwork and not EntityFramework)
            throw new ArgumentException($"Unknown execution placement store backend '{name}'.", nameof(name));
    }

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore))
            .ToArray();
        if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], _placementDescriptor))
        {
            throw new InvalidOperationException(
                $"Execution placement backend '{Name}' no longer exclusively owns IExecutionPlacementStore. " +
                $"Descriptors: {descriptors.Length}. Remove conflicting host registrations or select only the intended backend.");
        }
    }

    /// <summary>
    /// Removes backend-specific composition artifacts immediately before another backend takes ownership.
    /// </summary>
    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureOwnsRegisteredContract(services);
        _removeOwnedArtifacts?.Invoke(services);
    }
}
