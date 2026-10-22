namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a connection update was saved but a step that follows the save failed in the secret store: writing a
/// credential's new value, or deleting a secret the save stopped referencing. The update's other changes stay saved.
/// The store's failure travels as <see cref="Exception.InnerException"/> (an <see cref="AggregateException"/> when
/// several steps failed), so infrastructure exceptions never cross the connection boundary unwrapped. A write lost to
/// a concurrent writer is a <see cref="ConnectionConflictException"/> instead.
/// </summary>
public sealed class ConnectionCredentialWriteException : Exception
{
    public ConnectionCredentialWriteException(string connectionName, IReadOnlyCollection<string> fieldNames, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(fieldNames);
        ConnectionName = connectionName;
        FieldNames = fieldNames;
    }

    public string ConnectionName { get; }

    /// <summary>The credential fields whose new value was not written, or whose replaced secret was not deleted.</summary>
    public IReadOnlyCollection<string> FieldNames { get; }
}
