using System.Security.Cryptography;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Composes the Groundwork v2 runtime family over one adapter-owned provider connection and mints the
/// public store clients a workload drives. Bookmark lookup creates one composition per logical scope;
/// both compositions share the adapter-owned observer while retaining independent persistence contexts.
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
/// 3. The observer is registered as a singleton <see cref="IProviderCommandObserver"/>. Runtime stores
///    receive it through <c>GroundworkStorageSessionSource</c>; diagnostics stores receive it through their
///    composition features because they own their sessions and units of work directly. Both paths forward
///    the same observer to every provider command, so the measured path is production code rather than a
///    reconstruction.
/// </summary>
internal sealed class RuntimeStoreComposition : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly List<AsyncServiceScope> scopes = [];
    private readonly IStorageProviderConnection connection;
    private readonly bool ownsConnection;
    private readonly IReadOnlyList<IDiagnosticsPersistenceDrain> diagnosticsDrains;
    private readonly IReadOnlyList<IDiagnosticsPersistenceResourceLease> diagnosticsLeases;

    private RuntimeStoreComposition(
        ServiceProvider provider,
        IStorageProviderConnection connection,
        WritePathRoundTripObserver observer,
        bool ownsConnection,
        IReadOnlyList<IDiagnosticsPersistenceDrain>? diagnosticsDrains = null,
        IReadOnlyList<IDiagnosticsPersistenceResourceLease>? diagnosticsLeases = null)
    {
        this.provider = provider;
        this.connection = connection;
        this.ownsConnection = ownsConnection;
        Observer = observer;
        this.diagnosticsDrains = diagnosticsDrains ?? [];
        this.diagnosticsLeases = diagnosticsLeases ?? [];
    }

    public WritePathRoundTripObserver Observer { get; }

    /// <summary>Builds the same provider-neutral registration snapshot used by a live composition.</summary>
    internal static GroundworkStorageUnitRegistry CreateRegistry(
        BenchmarkCompositionFingerprint.CompositionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.IsGroundwork)
            throw new PerformanceContractException("Only Groundwork compositions have a storage-unit registry.");
        var services = new ServiceCollection();
        services.AddPersistenceCore("benchmark-composition");
        ConfigureOptionalStorageFamilies(services, selection);
        ConfigureGroundworkStorageFamilies(services, selection);
        return services
            .Single(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry
            ?? throw new PerformanceContractException("The benchmark composition registered no storage-unit registry.");
    }

    public static async Task<RuntimeStoreComposition> CreateAsync(
        string providerName,
        string connectionString,
        string persistenceScope,
        CancellationToken cancellationToken,
        WritePathRoundTripObserver? observer = null,
        IStorageProviderConnection? existingConnection = null,
        bool includeDistributedRuntimeStores = false,
        bool includeGroundworkIdentityStores = false,
        bool includeGroundworkSecretStores = false,
        bool includeGroundworkDiagnostics = false,
        StructuredLogStoreBinding? structuredLogBinding = null,
        GroundworkOpenTelemetryBinding? openTelemetryBinding = null,
        StructuredLogsOptions? structuredLogsOptions = null,
        OpenTelemetryDiagnosticsOptions? openTelemetryOptions = null)
    {
        observer ??= new WritePathRoundTripObserver(providerName);
        var ownsConnection = existingConnection is null;
        var connection = existingConnection ?? ProviderConnections.Open(providerName, connectionString);
        // Held so the catch can dispose it, and cleared once ownership transfers to the composition —
        // after that point DisposeAsync owns both it and the connection.
        ServiceProvider? built = null;
        try
        {
            var services = new ServiceCollection();

            // Registered before the runtime family so the units this connection must admit resolve against it.
            // A shared connection belongs to the adapter, not this composition's service provider. The
            // non-disposing facade prevents disposal of one logical scope from invalidating its sibling.
            services.AddGroundworkStorageProviderConnection(
                ownsConnection ? connection : new NonDisposingConnection(connection));

            // Registered before AddGroundworkV2RuntimeStores, which calls AddPersistenceCore with the
            // DEFAULT scope — and AddPersistenceCore registers with TryAddScoped, so whoever registers
            // first wins. The checkpoint writer's EnsureTenantScope compares each committed state's
            // TenantId against this ambient scope and refuses on mismatch, so a host composed with the
            // default scope cannot commit the workload's tenant-stamped states at all. Verified against a
            // live provider, not inferred: the first correctness run failed exactly there.
            services.AddPersistenceCore(persistenceScope);

            ConfigureOptionalStorageFamilies(
                services,
                new BenchmarkCompositionFingerprint.CompositionSelection(
                    true,
                    includeDistributedRuntimeStores,
                    includeGroundworkIdentityStores,
                    includeGroundworkSecretStores,
                    includeGroundworkDiagnostics,
                    []),
                structuredLogBinding,
                openTelemetryBinding,
                structuredLogsOptions,
                openTelemetryOptions);

            // Runtime's session source and diagnostics' direct-session features both resolve and forward
            // this observer. Singleton because the harness snapshots one cumulative count for the process,
            // and both clients must contribute to the same total.
            services.AddSingleton<IProviderCommandObserver>(observer);

            // Recovery continuations must be authenticated even though the key is only a local composition
            // fixture and never crosses the artifact boundary. A fresh cryptographic key per composition is
            // enough because every scanner/reopen client resolves the same singleton codec; no continuation is
            // persisted or handed to another benchmark process.
            var recoverySigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            services.Configure<RuntimeRecoveryContinuationOptions>(options =>
            {
                options.SigningKey = recoverySigningKey;
                options.AllowEphemeralDevelopmentKey = false;
            });

            ConfigureGroundworkStorageFamilies(
                services,
                new BenchmarkCompositionFingerprint.CompositionSelection(
                    true,
                    includeDistributedRuntimeStores,
                    includeGroundworkIdentityStores,
                    includeGroundworkSecretStores,
                    includeGroundworkDiagnostics,
                    []));

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
            var diagnosticsDrains = includeGroundworkDiagnostics
                ? built.GetServices<IDiagnosticsPersistenceDrain>().Distinct<IDiagnosticsPersistenceDrain>(ReferenceEqualityComparer.Instance).ToArray()
                : [];
            var diagnosticsLeases = new List<IDiagnosticsPersistenceResourceLease>();
            try
            {
                if (includeGroundworkDiagnostics)
                {
                    foreach (var resource in built.GetServices<IDiagnosticsPersistenceStartupResource>().Distinct<IDiagnosticsPersistenceStartupResource>(ReferenceEqualityComparer.Instance))
                        diagnosticsLeases.Add(await resource.AcquireAsync(cancellationToken));
                    foreach (var drain in diagnosticsDrains)
                        drain.Start();
                }
            }
            catch
            {
                foreach (var drain in diagnosticsDrains.Reverse())
                {
                    try { await drain.StopAsync(CancellationToken.None); } catch { }
                }
                foreach (var lease in diagnosticsLeases.AsEnumerable().Reverse())
                {
                    try { await lease.DisposeAsync(); } catch { }
                }
                throw;
            }
            var composition = new RuntimeStoreComposition(built, connection, observer, ownsConnection, diagnosticsDrains, diagnosticsLeases);
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
            if (ownsConnection)
                SafelyDispose(connection);
            throw;
        }
    }

    private static void ConfigureOptionalStorageFamilies(
        IServiceCollection services,
        BenchmarkCompositionFingerprint.CompositionSelection selection,
        StructuredLogStoreBinding? structuredLogBinding = null,
        GroundworkOpenTelemetryBinding? openTelemetryBinding = null,
        StructuredLogsOptions? structuredLogsOptions = null,
        OpenTelemetryDiagnosticsOptions? openTelemetryOptions = null)
    {
        if (selection.IncludeIdentity)
            services.AddFoundationAspNetCoreIdentityGroundwork();
        if (selection.IncludeSecrets)
            services.AddGroundworkSecretsStore();

        if (!selection.IncludeDiagnostics)
            return;

        // The domain features contribute their options, source registry and in-memory fallbacks; the
        // combined Groundwork feature then replaces both contracts atomically.
        new StructuredLogsFeature().ConfigureServices(services);
        new OpenTelemetryFeature().ConfigureServices(services);
        services.AddSingleton(structuredLogBinding ?? StructuredLogStoreBinding.Default);
        services.AddSingleton(openTelemetryBinding ?? GroundworkOpenTelemetryBinding.Default);
        if (structuredLogsOptions is not null)
            services.Configure<StructuredLogsOptions>(options => Copy(structuredLogsOptions, options));
        if (openTelemetryOptions is not null)
            services.Configure<OpenTelemetryDiagnosticsOptions>(options => Copy(openTelemetryOptions, options));
        new DiagnosticsGroundworkPersistenceFeature().ConfigureServices(services);
        foreach (var unit in V2OpenTelemetryStorageSchema.CreateUnits())
            services.AddGroundworkStorageUnit(unit);
        services.AddGroundworkStorageUnit(StructuredLogsGroundworkStorageSchema.CreateUnit());
    }

    private static void ConfigureGroundworkStorageFamilies(
        IServiceCollection services,
        BenchmarkCompositionFingerprint.CompositionSelection selection)
    {
        services.AddGroundworkV2RuntimeStores();
        if (selection.IncludeDistributed)
            services.AddGroundworkDistributedRuntimeStores();
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

    /// <summary>Mints the bookmark state and stimulus-index contracts from one isolated DI scope.</summary>
    public RuntimeBookmarkLookupClient CreateBookmarkClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeBookmarkLookupClient(
            services.GetRequiredService<IBookmarkStateStore>(),
            services.GetRequiredService<IBookmarkStimulusIndex>());
    }

    /// <summary>Mints the public recovery scanner and all state stores it correlates from an isolated scope.</summary>
    public RuntimeRecoveryScanClient CreateRecoveryScanClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeRecoveryScanClient(
            services.GetRequiredService<IRuntimeRecoveryScanner>(),
            services.GetRequiredService<IExecutionLivenessStateStore>(),
            services.GetRequiredService<IWorkflowExecutionStateStore>(),
            services.GetRequiredService<IIncidentStateStore>(),
            services.GetRequiredService<ISchedulerStateStore>(),
            services.GetRequiredService<IWorkflowHoldStateStore>());
    }

    /// <summary>Mints the scheduler queue and poison-store contracts from one isolated DI scope.</summary>
    public RuntimeQueueDrainClient CreateQueueClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeQueueDrainClient(
            services.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            services.GetRequiredService<IWorkflowSchedulerPoisonStore>(),
            services.GetRequiredService<IWorkflowSchedulerWorkClaimInspection>());
    }

    /// <summary>Mints the checkpoint and fenced post-commit outbox contracts from one isolated DI scope.</summary>
    public RuntimeOutboxDrainClient CreateOutboxClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeOutboxDrainClient(
            services.GetRequiredService<IRuntimeCheckpointCommitStore>(),
            services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>(),
            services.GetRequiredService<IRuntimePostCommitOutboxClaimCompletionStore>(),
            services.GetRequiredService<IPostCommitOutboxLookupStore>());
    }

    /// <summary>Mints the durable distributed execution placement contract from an isolated DI scope.</summary>
    public IExecutionPlacementStore CreatePlacementClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();
    }

    /// <summary>Mints the durable distributed execution command transport from an isolated DI scope.</summary>
    public IExecutionCommandTransport CreateCommandTransportClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();
    }

    /// <summary>Mints the durable runtime timer contract from an isolated DI scope.</summary>
    public IDurableTimerStore CreateDurableTimerClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<IDurableTimerStore>();
    }

    /// <summary>Mints the recurring-trigger schedule contract from an isolated DI scope.</summary>
    public IRecurringTriggerScheduleStore CreateRecurringScheduleClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>();
    }

    /// <summary>Mints the trigger-binding and executable-source-reference contracts from one isolated DI scope.</summary>
    public RuntimeTriggerBindingStimulusLookupScope CreateTriggerBindingClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeTriggerBindingStimulusLookupScope(
            services.GetRequiredService<IWorkflowTriggerBindingStore>(),
            services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
    }

    /// <summary>Mints the public ASP.NET Core Identity managers over the Groundwork-backed stores.</summary>
    public RuntimeIdentityClient CreateIdentityClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new RuntimeIdentityClient(
            services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole>>());
    }

    /// <summary>Mints the low-level Identity row seam used only to build the native-plan fixture.</summary>
    public Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkIdentityRowStore CreateIdentityRowStore()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkIdentityRowStore>();
    }

    /// <summary>Mints the public Secret repository over the Groundwork storage unit from an isolated scope.</summary>
    public ISecretRepository CreateSecretClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<ISecretRepository>();
    }

    /// <summary>Mints both diagnostics contracts from an isolated DI scope.</summary>
    public DiagnosticsStoreClient CreateDiagnosticsClient()
    {
        var scope = provider.CreateAsyncScope();
        scopes.Add(scope);
        var services = scope.ServiceProvider;
        return new(
            services.GetRequiredService<IStructuredLogStore>(),
            services.GetRequiredService<IOpenTelemetryStore>());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in scopes)
            await scope.DisposeAsync();
        scopes.Clear();
        foreach (var drain in diagnosticsDrains.Reverse())
            await drain.StopAsync(CancellationToken.None);
        foreach (var lease in diagnosticsLeases.Reverse())
            await lease.DisposeAsync();
        await provider.DisposeAsync();
        if (ownsConnection)
            connection.Dispose();
    }

    private static void Copy(StructuredLogsOptions source, StructuredLogsOptions target)
    {
        target.MinimumLevel = source.MinimumLevel;
        target.BufferCapacity = source.BufferCapacity;
        target.MaxRecentQuerySize = source.MaxRecentQuerySize;
        target.ShutdownDrainTimeout = source.ShutdownDrainTimeout;
    }

    private static void Copy(OpenTelemetryDiagnosticsOptions source, OpenTelemetryDiagnosticsOptions target)
    {
        target.TraceCapacity = source.TraceCapacity;
        target.SpanCapacity = source.SpanCapacity;
        target.MetricPointCapacity = source.MetricPointCapacity;
        target.LogRecordCapacity = source.LogRecordCapacity;
        target.ResourceCapacity = source.ResourceCapacity;
        target.MetricInstrumentCapacity = source.MetricInstrumentCapacity;
        target.SubscriberChannelCapacity = source.SubscriberChannelCapacity;
        target.MaxQuerySize = source.MaxQuerySize;
        target.ShutdownDrainTimeout = source.ShutdownDrainTimeout;
    }

    private sealed class NonDisposingConnection(IStorageProviderConnection inner) : IStorageProviderConnection
    {
        public IProviderCatalog Catalog => inner.Catalog;
        public ISchemaCoordinator Schema => inner.Schema;
        public IReadOnlyList<CapabilityDescriptor> Capabilities => inner.Capabilities;

        public IStorageSession OpenSession(StorageUnit unit, StorageAccess access, IProviderCommandObserver? observer = null) =>
            inner.OpenSession(unit, access, observer);

        public IOwnedStorageSession OpenOwnedSession(StorageUnit unit, StorageAccess access, IProviderCommandObserver? observer = null) =>
            inner.OpenOwnedSession(unit, access, observer);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) =>
            inner.BeginUnitOfWork(access, units);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, params StorageUnit[] units) =>
            inner.BeginUnitOfWork(access, options, units);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IProviderCommandObserver? observer,
            params StorageUnit[] units) =>
            inner.BeginUnitOfWork(access, options, observer, units);

        public void Dispose()
        {
            // The adapter owns and disposes the shared connection after all sibling compositions close.
        }
    }
}

internal sealed record DiagnosticsStoreClient(
    IStructuredLogStore StructuredLogs,
    IOpenTelemetryStore OpenTelemetry);

/// <summary>Public ASP.NET Core Identity manager contracts composed over the adapter's Groundwork stores.</summary>
internal sealed record RuntimeIdentityClient(
    UserManager<AspNetCoreIdentityUser> Users,
    RoleManager<IdentityRole> Roles);
