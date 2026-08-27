using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Composes the Groundwork v2 runtime family over one adapter-owned provider connection and mints the
/// public store clients the checkpoint workload drives.
///
/// Three things here are not obvious from the registration surface and are the whole reason this type
/// exists rather than the leaf calling <c>AddGroundworkV2RuntimeStores</c> inline:
///
/// 1. <see cref="GroundworkStorageProviderConnectionRegistration"/> is how an already-created connection
///    binds to a target. The provider packages own construction and lifetime, so the adapter opens the
///    connection itself (<see cref="ProviderConnections"/>) and hands the instance over.
/// 2. <c>GroundworkStorageSessionSource</c> admits its units from <c>IHostedService.StartAsync</c> /
///    <c>IShellInitializer.InitializeAsync</c>. A plain <c>BuildServiceProvider()</c> runs neither, so a
///    direct host must drive admission itself or every session resolves against an unadmitted unit.
/// 3. The observer is registered as a singleton <see cref="IProviderCommandObserver"/>, which
///    <c>GroundworkStorageSessionSource</c> resolves once for its lifetime and forwards to every session
///    and unit of work it opens — both forwarding points, because the commit path runs through units of
///    work. That is what makes the measured path the production commit path rather than a reconstruction.
/// </summary>
internal sealed class RuntimeStoreComposition : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly List<AsyncServiceScope> scopes = [];
    private readonly IStorageProviderConnection connection;

    private RuntimeStoreComposition(
        ServiceProvider provider,
        IStorageProviderConnection connection,
        WritePathRoundTripObserver observer)
    {
        this.provider = provider;
        this.connection = connection;
        Observer = observer;
    }

    public WritePathRoundTripObserver Observer { get; }

    public static async Task<RuntimeStoreComposition> CreateAsync(
        string providerName,
        string connectionString,
        string persistenceScope,
        CancellationToken cancellationToken)
    {
        var observer = new WritePathRoundTripObserver(providerName);
        var connection = ProviderConnections.Open(providerName, connectionString);
        // Held so the catch can dispose it, and cleared once ownership transfers to the composition —
        // after that point DisposeAsync owns both it and the connection.
        ServiceProvider? built = null;
        try
        {
            var services = new ServiceCollection();

            // Registered before the runtime family so the units this connection must admit resolve against it.
            services.AddGroundworkStorageProviderConnection(connection);

            // Registered before AddGroundworkV2RuntimeStores, which calls AddPersistenceCore with the
            // DEFAULT scope — and AddPersistenceCore registers with TryAddScoped, so whoever registers
            // first wins. The checkpoint writer's EnsureTenantScope compares each committed state's
            // TenantId against this ambient scope and refuses on mismatch, so a host composed with the
            // default scope cannot commit the workload's tenant-stamped states at all. Verified against a
            // live provider, not inferred: the first correctness run failed exactly there.
            services.AddPersistenceCore(persistenceScope);

            // The observer GroundworkStorageSessionSource resolves and forwards to every session and unit
            // of work it opens. Singleton because the harness snapshots one cumulative count for the
            // process, and both clients must contribute to the same total.
            services.AddSingleton<IProviderCommandObserver>(observer);

            services.AddGroundworkV2RuntimeStores();

            // IRuntimeExecutionOwnershipService has no Groundwork replacement; it comes from the runtime core.
            services.AddWorkflowRuntime();

            built = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = false
            });

            // Admission is real database work — schema inspection per unit — so it is a genuine failure
            // point, not a formality. If it throws, the provider it was resolved from must be disposed
            // here: nothing else holds a reference to it yet, and it owns singletons of its own.
            await built.GetRequiredService<GroundworkStorageSessionSource>().InitializeAsync(cancellationToken);
            var composition = new RuntimeStoreComposition(built, connection, observer);
            built = null;
            return composition;
        }
        catch
        {
            // Cleanup is best-effort on both handles, and deliberately so. Disposing the provider first
            // without guarding it would mean a throwing DisposeAsync skipped the connection entirely and
            // replaced the admission failure — the thing the caller actually needs to see — with a far less
            // diagnostic cleanup error. Each is therefore released independently, and the original
            // exception is the one that propagates.
            await SafelyDisposeAsync(built);
            SafelyDispose(connection);
            throw;
        }
    }

    private static async ValueTask SafelyDisposeAsync(ServiceProvider? provider)
    {
        try
        {
            if (provider is not null)
                await provider.DisposeAsync();
        }
        catch
        {
            // Swallowed so the failure being handled survives; see the catch block above.
        }
    }

    private static void SafelyDispose(IStorageProviderConnection connection)
    {
        try
        {
            connection.Dispose();
        }
        catch
        {
            // Swallowed so the failure being handled survives; see the catch block above.
        }
    }

    /// <summary>
    /// Mints a client in its own scope. The workload enforces that its two clients are genuinely distinct
    /// (<c>RequireIndependentClients</c>), so every call must open a new scope: the runtime stores are
    /// scoped registrations, and two clients from one scope would share instances and fail admission.
    /// </summary>
    public RuntimeCheckpointCommitClient CreateClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeCheckpointCommitClient(
            services.GetRequiredService<IRuntimeCheckpointCommitStore>(),
            services.GetRequiredService<IRuntimeExecutionOwnershipService>(),
            services.GetRequiredService<IWorkflowExecutableStore>(),
            services.GetRequiredService<IWorkflowExecutionStateStore>(),
            services.GetRequiredService<IActivityExecutionStateStore>(),
            services.GetRequiredService<IDurableValueStateStore>(),
            services.GetRequiredService<IRuntimePostCommitOutboxStore>());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in scopes)
            await scope.DisposeAsync();
        scopes.Clear();
        await provider.DisposeAsync();
        connection.Dispose();
    }
}
