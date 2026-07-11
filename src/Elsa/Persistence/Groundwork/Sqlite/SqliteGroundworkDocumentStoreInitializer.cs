using System.Diagnostics;
using CShells.Lifecycle;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Sqlite.Documents;
using Microsoft.Data.Sqlite;
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
    GroundworkDocumentStoreHolder holder,
    bool rematerializeOnStartup = false) : IHostedService, IShellInitializer
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

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
            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (holder.IsInitialized)
                {
                    outcome = SqliteGroundworkTelemetry.HistoryHitOutcome;
                    return;
                }

                SqliteDocumentStoreHandle? handle = null;
                if (!rematerializeOnStartup)
                    handle = await TryOpenFromExactHistoryAsync(cancellationToken);

                if (handle is not null)
                {
                    outcome = SqliteGroundworkTelemetry.HistoryHitOutcome;
                }
                else
                {
                    handle = await SqliteDocumentStoreFactory.CreateAsync(
                        connectionString,
                        manifest,
                        provider,
                        cancellationToken: cancellationToken);
                    outcome = SqliteGroundworkTelemetry.MaterializedOutcome;
                }

                holder.Set(handle.Store, handle);
            }
            finally
            {
                _initializationGate.Release();
            }
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

    private async Task<SqliteDocumentStoreHandle?> TryOpenFromExactHistoryAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = 'groundwork_schema_history'
                LIMIT 1;
                """;
            if (await tableCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                await connection.DisposeAsync();
                return null;
            }

            await using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = """
                SELECT 1
                FROM groundwork_schema_history
                WHERE manifest_id = $manifestId
                  AND manifest_version = $manifestVersion
                  AND provider_name = $providerName
                  AND provider_version = $providerVersion
                LIMIT 1;
                """;
            historyCommand.Parameters.AddWithValue("$manifestId", manifest.Identity.Value);
            historyCommand.Parameters.AddWithValue("$manifestVersion", manifest.Version.Value);
            historyCommand.Parameters.AddWithValue("$providerName", provider.Name);
            historyCommand.Parameters.AddWithValue("$providerVersion", provider.Version);

            if (await historyCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                await connection.DisposeAsync();
                return null;
            }

            var store = new SqliteDocumentStore(connection, manifest);
            return new SqliteDocumentStoreHandle(connection, store);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
