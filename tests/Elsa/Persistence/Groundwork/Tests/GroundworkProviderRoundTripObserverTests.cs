using Elsa.Persistence.Groundwork.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkProviderRoundTripObserverTests
{
    [Fact]
    public async Task Sqlite_observer_counts_commands_on_the_attached_provider_connection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = GroundworkProviderRoundTripObserver.TryCreate("sqlite");

        Assert.NotNull(observer);
        Assert.True(observer!.IsExact);
        Assert.Equal("sqlite3_trace", observer.Instrumentation);
        observer.Attach(connection);

        var before = observer.Snapshot();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

        Assert.True(observer.Snapshot() > before);
        Assert.Null(GroundworkProviderRoundTripObserver.TryCreate("postgresql"));
    }
}
