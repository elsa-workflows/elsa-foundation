namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// The retained artifact requires a stable durable-value storage-driver capability that is not installed.
/// This is a deployment compatibility failure, not an ordinary value or activity failure.
/// </summary>
public sealed class RuntimeDurableValueStorageDriverNotFoundException(string driverKey)
    : Exception($"No durable-value storage driver is registered for key '{driverKey}'.")
{
    public string DriverKey { get; } = driverKey;
}
