using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Runtime-owned registry for stable durable-value storage-driver keys.</summary>
public sealed class RuntimeDurableValueStorageDriverRegistry : IRuntimeDurableValueStorageDriverRegistry
{
    private readonly IReadOnlyDictionary<string, IRuntimeDurableValueStorageDriver> _drivers;

    public RuntimeDurableValueStorageDriverRegistry(IEnumerable<IRuntimeDurableValueStorageDriver> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        var snapshot = new Dictionary<string, IRuntimeDurableValueStorageDriver>(StringComparer.Ordinal);
        foreach (var driver in drivers)
        {
            ArgumentNullException.ThrowIfNull(driver);
            ArgumentException.ThrowIfNullOrWhiteSpace(driver.DriverKey);
            if (!snapshot.TryAdd(driver.DriverKey, driver))
                throw new InvalidOperationException($"Durable-value storage driver '{driver.DriverKey}' is registered more than once.");
        }

        _drivers = snapshot;
    }

    public IRuntimeDurableValueStorageDriver GetRequired(string driverKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverKey);
        return _drivers.TryGetValue(driverKey, out var driver)
            ? driver
            : throw new InvalidOperationException($"No durable-value storage driver is registered for key '{driverKey}'.");
    }
}

/// <summary>Built-in JSON codec that stores the encoded value inline.</summary>
public sealed class JsonRuntimeDurableValueStorageDriver : IRuntimeDurableValueStorageDriver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string DriverKey => WellKnownRuntimeDurableValueStorageDrivers.Json;

    public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(
        object? value,
        RuntimeValueTypeDescriptor type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var encoded = value is JsonElement element
                ? element.Clone()
                : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), SerializerOptions);
            return ValueTask.FromResult(new RuntimeDurableValueEncoding(DurableValueStorage.Inline, encoded, null));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Value of runtime type '{value?.GetType().FullName ?? "null"}' cannot be encoded by durable-value storage driver '{DriverKey}'.",
                exception);
        }
    }

    public ValueTask<object?> DecodeAsync(
        DurableValueState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(state.Type.Id, DriverKey))
            throw new InvalidOperationException($"Durable value '{state.DurableValueId}' is not encoded for storage driver '{DriverKey}'.");
        if (state.Storage != DurableValueStorage.Inline || state.InlineValue is not { } inlineValue)
            throw new InvalidOperationException($"Durable value '{state.DurableValueId}' is not valid inline JSON state.");

        return ValueTask.FromResult<object?>(inlineValue.Clone());
    }
}
