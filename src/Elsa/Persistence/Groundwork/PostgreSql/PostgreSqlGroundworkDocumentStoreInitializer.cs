using CShells.Lifecycle;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Scoping;
using Groundwork.PostgreSql.Documents;
using Elsa.Persistence.Groundwork.Querying;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.PostgreSql;

/// <summary>
/// Materializes the one PostgreSQL-backed Groundwork document store at host startup and populates the shared
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
public sealed class PostgreSqlGroundworkDocumentStoreInitializer(
    string connectionString,
    StorageManifest manifest,
    ProviderIdentity provider,
    GroundworkDocumentStoreHolder holder,
    IGroundworkWorkflowExecutionStatePageQuery historyQuery) : IHostedService, IShellInitializer
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            if (!holder.IsInitialized)
            {
                var store = await PostgreSqlDocumentStoreFactory.CreateAsync(
                    connectionString,
                    manifest,
                    provider,
                    DocumentStoreAccess.Global,
                    cancellationToken: cancellationToken);
                holder.Set(store);
            }

            await historyQuery.PrepareAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}
