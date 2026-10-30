using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Typed runtime fault when a referenced connection is missing, the wrong type, revoked, or in
/// <see cref="ConnectionStatuses.NeedsAuthorization"/>. Generic retry strategies must not retry it.
/// </summary>
public sealed class ConnectionUnavailableException : Exception
{
    public ConnectionUnavailableException(string connectionName, string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ConnectionName = connectionName;
        Code = code;
    }

    public string ConnectionName { get; }

    public string Code { get; }

    public bool IsRetryable => false;
}
