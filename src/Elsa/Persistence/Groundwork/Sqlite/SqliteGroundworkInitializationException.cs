namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Reports a failure to inspect, materialize, or open the SQLite Groundwork document store.
/// </summary>
public sealed class SqliteGroundworkInitializationException(string message, Exception innerException)
    : Exception(message, innerException);
