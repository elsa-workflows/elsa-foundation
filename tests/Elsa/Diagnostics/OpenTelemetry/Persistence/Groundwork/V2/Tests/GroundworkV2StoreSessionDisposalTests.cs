using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

/// <summary>
/// Issue #1597: every session the store opens holds a provider connection; disposing the store must
/// dispose them, otherwise a host that recreates the store (the benchmark's reopened compositions, or a
/// shell restart) exhausts pooled providers.
/// </summary>
public sealed class GroundworkV2StoreSessionDisposalTests
{
    [Fact]
    public async Task Disposing_the_store_disposes_every_session_it_opened()
    {
        using var database = new TemporarySqliteDatabase();
        using var real = database.Connection;
        var recording = SessionRecordingConnection.Wrap(real, out var opened);
        var store = new GroundworkOpenTelemetryStore(
            recording,
            Options.Create(new OpenTelemetryDiagnosticsOptions { TraceCapacity = 1, MaxQuerySize = 100 }),
            V2OpenTelemetryBinding.Default);
        await using (await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync())
            store.Start();
        Assert.Equal(8, opened.Count);

        await store.DisposeAsync();

        Assert.All(opened, session => Assert.True(((IOwnedStorageSession)session).IsReleased));
    }

    [Fact]
    public async Task Releasing_the_startup_lease_disposes_the_sessions_it_opened()
    {
        using var database = new TemporarySqliteDatabase();
        using var real = database.Connection;
        var recording = SessionRecordingConnection.Wrap(real, out var opened);
        await using var store = new GroundworkOpenTelemetryStore(
            recording,
            Options.Create(new OpenTelemetryDiagnosticsOptions { TraceCapacity = 1, MaxQuerySize = 100 }),
            V2OpenTelemetryBinding.Default);

        var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        Assert.Equal(8, opened.Count);
        await lease.DisposeAsync();

        Assert.All(opened, session => Assert.True(((IOwnedStorageSession)session).IsReleased));
    }
}
