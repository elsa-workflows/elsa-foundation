namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a connection create or update fails in the connection store or the secret store before it is confirmed
/// saved, so the connection was not saved and no change is confirmed committed. Retrying usually works, but the message
/// says when it cannot (a misconfigured secret store). A failed create makes a best-effort attempt to remove what it
/// wrote and logs any step it could not complete. The store's failure travels as
/// <see cref="Exception.InnerException"/>, so infrastructure exceptions never cross the connection boundary unwrapped.
/// A failure after the save is a <see cref="ConnectionCredentialWriteException"/> instead, and a lost race or an
/// owned secret name that a deleted secret still holds is a <see cref="ConnectionConflictException"/>. A secret
/// created for a field moving to inline storage before the failure is not rolled back on an update (see the
/// Elsa.Connections extension points).
/// </summary>
public sealed class ConnectionStoreException : Exception
{
    public ConnectionStoreException(string connectionName, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ConnectionName = connectionName;
    }

    public string ConnectionName { get; }
}
