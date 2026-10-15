using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Sdk;

/// <summary>
/// Typed fault when a referenced service connection is missing, the wrong type, or not ready
/// (plan D7). Generic retry strategies must not retry it: retrying cannot create the connection.
/// </summary>
public sealed class ConnectionUnavailableException : Exception
{
    public const string FaultCode = "ConnectionUnavailable";
    public const string FaultTypeName = "ConnectionUnavailable";
    public const string FaultCategory = "connection";

    public ConnectionUnavailableException(string message, string? connectionName = null, bool isRetryable = false)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ConnectionName = connectionName;
        IsRetryable = isRetryable;
    }

    public string? ConnectionName { get; }

    public bool IsRetryable { get; }

    public ActivityFault ToFault() =>
        new(FaultCode, Message, IsRetryable, category: FaultCategory, faultType: FaultTypeName);
}
