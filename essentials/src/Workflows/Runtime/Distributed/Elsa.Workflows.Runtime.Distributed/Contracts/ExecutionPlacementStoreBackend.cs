using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Registers the selected backend marker and a startup validator that re-checks exclusive
    /// ownership after all host registrations have been applied.
    /// </summary>
    public static void Register(IServiceCollection services, ExecutionPlacementStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);

        services.AddSingleton(backend);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ExecutionPlacementStoreRegistrationState)))
            return;

        var state = new ExecutionPlacementStoreRegistrationState(services);
        services.AddSingleton(state);
        services.AddSingleton<IValidateOptions<ExecutionPlacementStoreRegistrationOptions>>(
            new ExecutionPlacementStoreRegistrationValidator(state));
        services.AddOptions<ExecutionPlacementStoreRegistrationOptions>().ValidateOnStart();
    }

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
        if (name is not InMemory and not EntityFramework)
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

internal sealed record ExecutionPlacementStoreRegistrationState(IServiceCollection Services);

internal sealed class ExecutionPlacementStoreRegistrationOptions;

internal sealed class ExecutionPlacementStoreRegistrationValidator(
    ExecutionPlacementStoreRegistrationState state) : IValidateOptions<ExecutionPlacementStoreRegistrationOptions>
{
    public ValidateOptionsResult Validate(string? name, ExecutionPlacementStoreRegistrationOptions options)
    {
        try
        {
            var marker = ExecutionPlacementStoreBackend.Find(state.Services);
            if (marker is null)
                return ValidateOptionsResult.Fail("The execution placement backend ownership marker was removed after registration.");

            marker.EnsureOwnsRegisteredContract(state.Services);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
