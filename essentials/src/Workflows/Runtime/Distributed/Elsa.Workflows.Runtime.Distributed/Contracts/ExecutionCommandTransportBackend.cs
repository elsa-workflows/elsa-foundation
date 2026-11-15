using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>Records the exclusive owner of the distributed command-transport contract.</summary>
public sealed class ExecutionCommandTransportBackend
{
    private readonly ServiceDescriptor _transportDescriptor;
    private readonly Action<IServiceCollection>? _removeOwnedArtifacts;

    public const string InMemory = "in-memory";
    public const string EntityFramework = "entity-framework";

    public ExecutionCommandTransportBackend(
        string name,
        ServiceDescriptor transportDescriptor,
        Action<IServiceCollection>? removeOwnedArtifacts = null)
    {
        EnsureKnown(name);
        ArgumentNullException.ThrowIfNull(transportDescriptor);
        if (transportDescriptor.ServiceType != typeof(IExecutionCommandTransport))
            throw new ArgumentException("The owned descriptor must register IExecutionCommandTransport.", nameof(transportDescriptor));
        Name = name;
        _transportDescriptor = transportDescriptor;
        _removeOwnedArtifacts = removeOwnedArtifacts;
    }

    public string Name { get; }

    public static void Register(IServiceCollection services, ExecutionCommandTransportBackend backend)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        services.AddSingleton(backend);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ExecutionCommandTransportRegistrationState)))
            return;
        var state = new ExecutionCommandTransportRegistrationState(services);
        services.AddSingleton(state);
        services.AddSingleton<IValidateOptions<ExecutionCommandTransportRegistrationOptions>>(
            new ExecutionCommandTransportRegistrationValidator(state));
        services.AddOptions<ExecutionCommandTransportRegistrationOptions>().ValidateOnStart();
    }

    public static ExecutionCommandTransportBackend? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<ExecutionCommandTransportBackend>()
        .SingleOrDefault();

    public static bool HasRegisteredContract(IServiceCollection services) => services
        .Any(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));

    public static void EnsureKnown(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not InMemory and not EntityFramework)
            throw new ArgumentException($"Unknown execution command transport backend '{name}'.", nameof(name));
    }

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)).ToArray();
        if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], _transportDescriptor))
            throw new InvalidOperationException(
                $"Execution command transport backend '{Name}' no longer exclusively owns IExecutionCommandTransport. Descriptors: {descriptors.Length}.");
    }

    public void RemoveOwnedArtifacts(IServiceCollection services)
    {
        EnsureOwnsRegisteredContract(services);
        _removeOwnedArtifacts?.Invoke(services);
    }
}

internal sealed record ExecutionCommandTransportRegistrationState(IServiceCollection Services);
internal sealed class ExecutionCommandTransportRegistrationOptions;

internal sealed class ExecutionCommandTransportRegistrationValidator(
    ExecutionCommandTransportRegistrationState state) : IValidateOptions<ExecutionCommandTransportRegistrationOptions>
{
    public ValidateOptionsResult Validate(string? name, ExecutionCommandTransportRegistrationOptions options)
    {
        try
        {
            var marker = ExecutionCommandTransportBackend.Find(state.Services);
            if (marker is null)
                return ValidateOptionsResult.Fail("The execution command transport backend ownership marker was removed after registration.");
            marker.EnsureOwnsRegisteredContract(state.Services);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
