using System.Diagnostics;
using CShells.Lifecycle;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Materializes the one SQLite-backed Groundwork document store at host startup and populates the shared
/// <see cref="GroundworkDocumentStoreHolder"/>, so <see cref="Groundwork.Documents.Store.IDocumentStore"/> can be
/// resolved as a fully-initialized singleton without a synchronous block on the resolving thread.
/// </summary>
/// <remarks>
/// Implemented as both an <see cref="IHostedService"/> (plain hosts / tests) and a CShells
/// <see cref="IShellInitializer"/> (the shell-composed Elsa.Server host, where shell-scoped hosted services do
/// not run) — the same dual-hook pattern the identity module uses. The provider registration schedules it in the
/// <see cref="LifecyclePhase.Prepare"/> phase so the store is ready before any other shell initializer that reads
/// it. Population is idempotent, so running under either hook is safe.
/// </remarks>
public sealed class SqliteGroundworkDocumentStoreInitializer(
    string connectionString,
    StorageManifest manifest,
    ProviderIdentity provider,
    GroundworkDocumentStoreHolder holder) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = holder.IsInitialized
            ? SqliteGroundworkTelemetry.HistoryHitOutcome
            : SqliteGroundworkTelemetry.MaterializedOutcome;
        using var activity = SqliteGroundworkTelemetry.ActivitySource.StartActivity(SqliteGroundworkTelemetry.ActivityName);

        try
        {
            if (holder.IsInitialized)
                return;

            var handle = await SqliteDocumentStoreFactory.CreateAsync(connectionString, manifest, provider, cancellationToken: cancellationToken);
            holder.Set(handle.Store, handle);
        }
        catch (OperationCanceledException)
        {
            outcome = SqliteGroundworkTelemetry.CancelledOutcome;
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception)
        {
            outcome = SqliteGroundworkTelemetry.FailedOutcome;
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            activity?.SetTag(SqliteGroundworkTelemetry.OutcomeTag, outcome);
            SqliteGroundworkTelemetry.Duration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>(SqliteGroundworkTelemetry.OutcomeTag, outcome));
        }
    }
}
