namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a connection create or update fails in the connection store or the secret store before it is confirmed
/// saved, so no change is confirmed committed (a store can commit and then fail). Retrying usually works, but the
/// message says when it cannot (a misconfigured secret store). A failed create or update makes a best-effort attempt
/// to remove the secrets it created, and a failed create its connection row, keeping what a committed save
/// references; it logs any step it could not complete. The store's failure travels as
/// <see cref="Exception.InnerException"/>, so infrastructure exceptions never cross the connection boundary unwrapped.
/// A failure after the save is a <see cref="ConnectionCredentialWriteException"/> instead, and a lost race is a
/// <see cref="ConnectionConflictException"/>.
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
