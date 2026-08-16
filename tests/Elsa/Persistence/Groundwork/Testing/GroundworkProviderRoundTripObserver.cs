using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Provider-native command observer used by the E3 measurement harness.
/// SQLite's trace callback fires for each statement executed on the actual provider connection;
/// adapter method calls are deliberately not counted as round trips.
/// </summary>
public sealed class GroundworkProviderRoundTripObserver
{
    private static readonly strdelegate_trace Trace = OnTrace;
    private long _statementCount;

    private GroundworkProviderRoundTripObserver(string provider)
    {
        Provider = provider;
    }

    public string Provider { get; }
    public string Instrumentation => "sqlite3_trace";
    public bool IsExact => string.Equals(Provider, "sqlite", StringComparison.Ordinal);

    public static GroundworkProviderRoundTripObserver? TryCreate(string provider) =>
        string.Equals(provider, "sqlite", StringComparison.Ordinal)
            ? new GroundworkProviderRoundTripObserver(provider)
            : null;

    public long Snapshot() => Interlocked.Read(ref _statementCount);

    public void Attach(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var handle = connection.Handle
                     ?? throw new InvalidOperationException("The SQLite connection must be open before command observation is attached.");
        raw.sqlite3_trace(handle, Trace, this);
    }

    private static void OnTrace(object state, string _)
    {
        if (state is GroundworkProviderRoundTripObserver observer)
            Interlocked.Increment(ref observer._statementCount);
    }
}
