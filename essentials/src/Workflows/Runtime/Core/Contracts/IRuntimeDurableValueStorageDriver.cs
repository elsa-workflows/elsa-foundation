using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Encodes a runtime value according to one stable durable-value storage-driver contract.
/// Implementations may produce inline, external, or custom durable state.
/// </summary>
public interface IRuntimeDurableValueStorageDriver
{
    string DriverKey { get; }

    ValueTask<RuntimeDurableValueEncoding> EncodeAsync(
        object? value,
        RuntimeValueTypeDescriptor type,
        CancellationToken cancellationToken = default);

    ValueTask<object?> DecodeAsync(
        DurableValueState state,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves durable-value storage drivers by their stable runtime key.</summary>
public interface IRuntimeDurableValueStorageDriverRegistry
{
    IRuntimeDurableValueStorageDriver GetRequired(string driverKey);
    bool Contains(string driverKey);
    IReadOnlyCollection<string> DriverKeys { get; }
}
