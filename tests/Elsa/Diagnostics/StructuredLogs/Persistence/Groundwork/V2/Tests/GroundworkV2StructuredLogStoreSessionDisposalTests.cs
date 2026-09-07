using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests;

/// <summary>
/// Issue #1597: the session the connection-based store opens at startup holds a provider connection;
/// releasing the startup lease or disposing the store must dispose it.
/// </summary>
public sealed class GroundworkV2StructuredLogStoreSessionDisposalTests
{
    private static readonly StructuredLogStoreBinding Binding = new("tenant", "scope", "structured-logs");

    [Fact]
    public async Task Releasing_the_startup_lease_disposes_the_session_it_opened()
    {
        using var database = new TemporarySqliteDatabase();
        using var real = database.Connection;
        await using var store = CreateStore(real, out var opened);

        var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        var session = Assert.Single(opened);
        await lease.DisposeAsync();

        Assert.True(((IOwnedStorageSession)session).IsReleased);
    }

    [Fact]
    public async Task Disposing_the_store_disposes_the_session_but_stopping_keeps_it_readable()
    {
        using var database = new TemporarySqliteDatabase();
        using var real = database.Connection;
        var store = CreateStore(real, out var opened);
        await using (await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync())
        {
            await store.StopAsync();
            Assert.Empty(await store.GetRecentAsync(StructuredLogFilter.None));
        }
        var session = Assert.Single(opened);

        await store.DisposeAsync();

        Assert.True(((IOwnedStorageSession)session).IsReleased);
    }

    [Fact]
    public async Task Disposing_a_store_that_never_started_is_harmless()
    {
        using var database = new TemporarySqliteDatabase();
        using var real = database.Connection;
        var store = CreateStore(real, out var opened);

        await store.DisposeAsync();

        Assert.Empty(opened);
    }

    private static GroundworkStructuredLogStore CreateStore(IStorageProviderConnection real, out IReadOnlyList<IStorageSession> opened) =>
        new(SessionRecordingConnection.Wrap(real, out opened), Options.Create(new StructuredLogsOptions()), Binding);
}
