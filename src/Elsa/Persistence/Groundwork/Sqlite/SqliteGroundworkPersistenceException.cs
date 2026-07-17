namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Identifies the SQLite Groundwork persistence operation that failed.
/// </summary>
public enum SqliteGroundworkPersistenceOperation
{
    /// <summary>The host-selected physical schema or provider session source was being initialized.</summary>
    Initialize,

    /// <summary>An access-bound SQLite Groundwork store session was being opened.</summary>
    OpenSession
}

/// <summary>
/// Represents an infrastructure failure at the SQLite Groundwork persistence boundary.
/// </summary>
/// <remarks>
/// The public message deliberately omits provider connection details. <see cref="Operation"/> supplies stable
/// domain context and <see cref="Exception.InnerException"/> preserves the originating provider failure for
/// trusted diagnostics. Cancellation is never wrapped in this exception.
/// </remarks>
public sealed class SqliteGroundworkPersistenceException(
    SqliteGroundworkPersistenceOperation operation,
    Exception innerException)
    : InvalidOperationException(
        $"SQLite Groundwork persistence operation '{operation}' failed; provider and connection details were suppressed.",
        innerException)
{
    /// <summary>Gets the persistence operation that failed.</summary>
    public SqliteGroundworkPersistenceOperation Operation { get; } = operation;
}
