using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Typed runtime fault when a referenced connection is missing, the wrong type, revoked, or in
/// <see cref="ConnectionStatuses.NeedsAuthorization"/>. Generic retry strategies must not retry it.
/// The activity fault recorder reads retryability and the failure code through
/// <see cref="IRuntimeFaultClassification"/>.
/// </summary>
public sealed class ConnectionUnavailableException : Exception, IRuntimeFaultClassification
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

    string? IRuntimeFaultClassification.FailureCode => Code;
}
