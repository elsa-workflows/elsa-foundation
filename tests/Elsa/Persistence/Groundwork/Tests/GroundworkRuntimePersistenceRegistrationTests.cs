using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using CShells.Lifecycle;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Groundwork.Core.Transactions;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimePersistenceRegistrationTests
{
    private static readonly Type[] LogicBearingRuntimeServiceTypes =
    [
        typeof(IGroundworkStorageManifestSource),
        typeof(IBookmarkStateStore),
        typeof(IWorkflowExecutableStore),
        typeof(IExecutableActivityTemplateStore),
        typeof(IWorkflowExecutableSourceReferenceStore),
        typeof(IActivityExecutionStateStore),
        typeof(GroundworkActivityExecutionInspectionStore),
        typeof(IActivityExecutionInspectionStore),
        typeof(IActivityExecutionInspectionWriter),
        typeof(IActivityExecutionHierarchyStore),
        typeof(IActivityExecutionHierarchyReader),
        typeof(IActivityExecutionHierarchyWriter),
        typeof(IWorkflowExecutionStateStore),
        typeof(IDurableValueStateStore),
        typeof(ISchedulerStateStore),
        typeof(IExecutionLivenessStateStore),
        typeof(IWorkflowHoldStateStore),
        typeof(IIncidentStateStore),
        typeof(IRuntimeCheckpointCommitStore),
        typeof(IRuntimePostCommitOutboxStore),
        typeof(GroundworkRuntimePostCommitOutboxStore),
        typeof(IRuntimePostCommitOutboxClaimStore),
        typeof(IRuntimePostCommitOutboxClaimCompletionStore),
        typeof(GroundworkWorkflowDispatchStore),
        typeof(IWorkflowDispatchStore),
        typeof(IWorkflowDispatchQueryStore),
        typeof(IWorkflowDispatchDeleteStore),
        typeof(IWorkflowDispatchRetentionRootStore),
        typeof(IWorkflowSchedulerWorkQueue),
        typeof(IWorkflowSchedulerPoisonStore),
        typeof(IDurableTimerStore),
        typeof(IWorkflowTriggerBindingStore),
        typeof(IRecurringTriggerScheduleStore)
    ];

    [Fact]
    public void AddGroundworkRuntimeStores_Registers_Logic_Bearing_Services_As_Scoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddGroundworkRuntimeStores();

        Assert.All(LogicBearingRuntimeServiceTypes, serviceType =>
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        });
    }

    [Fact]
    public void AddGroundworkRuntimeStores_Contributes_Durable_Dispatch_Readiness_Evidence()
    {
        var services = new ServiceCollection();
        var sessionSource = new GroundworkStoreSessionSource();
        Assert.True(sessionSource.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("Readiness must not open a provider session."),
            TransactionBoundary.CrossUnitAtomic));
        services.AddSingleton(sessionSource);

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();
        var evidence = provider.GetServices<IWorkflowDispatchDurabilityEvidence>()
            .ToDictionary(item => item.Component, item => item.Level, StringComparer.Ordinal);

        Assert.Equal(4, evidence.Count);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence[WorkflowDispatchDurabilityComponents.Checkpoint]);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence[WorkflowDispatchDurabilityComponents.DispatchStore]);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence[WorkflowDispatchDurabilityComponents.Outbox]);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence[WorkflowDispatchDurabilityComponents.Scheduler]);
    }

    [Fact]
    public void AddGroundworkRuntimeStores_DoesNotClaimDurableCheckpointWithoutAdmittedAtomicBoundary()
    {
        var services = new ServiceCollection();
        var sessionSource = new GroundworkStoreSessionSource();
        Assert.True(sessionSource.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("Readiness must not open a provider session."),
            TransactionBoundary.PerOperation));
        services.AddSingleton(sessionSource);
        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();
        var evidence = provider.GetServices<IWorkflowDispatchDurabilityEvidence>()
            .ToDictionary(item => item.Component, item => item.Level, StringComparer.Ordinal);

        Assert.Equal(
            WorkflowDispatchDurabilityLevel.ProcessLocal,
            evidence[WorkflowDispatchDurabilityComponents.Checkpoint]);
    }

    [Fact]
    public void Independent_Request_Scopes_Do_Not_Share_Runtime_Adapter_Instances()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        services.AddSingleton<IDocumentStore>(documentStore);
        services.AddSingleton<IBoundedDocumentStore>(documentStore);
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughRootWriteLeaseManager.Instance);
        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var firstRequest = provider.CreateScope();
        using var secondRequest = provider.CreateScope();

        AssertScopedWithinRequestButIsolatedAcrossRequests<IBookmarkStateStore>(firstRequest, secondRequest);
        AssertScopedWithinRequestButIsolatedAcrossRequests<IRuntimeCheckpointCommitStore>(firstRequest, secondRequest);
    }

    [Fact]
    public void Default_Runtime_Composition_Keeps_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IExecutableActivityTemplateStore, InMemoryExecutableActivityTemplateStore>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<InMemoryWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<InMemoryExecutableActivityTemplateStore>(provider.GetRequiredService<IExecutableActivityTemplateStore>());
    }

    [Fact]
    public void AddGroundworkRuntimeStores_Replaces_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        services.TryAddSingleton<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();
        services.TryAddSingleton<IWorkflowSchedulerPoisonStore, InMemoryWorkflowSchedulerPoisonStore>();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        services.AddWorkflowRuntime();

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkExecutableActivityTemplateStore>(provider.GetRequiredService<IExecutableActivityTemplateStore>());
        Assert.IsType<GroundworkActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<GroundworkWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<GroundworkDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<GroundworkSchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.IsType<GroundworkExecutionLivenessStateStore>(provider.GetRequiredService<IExecutionLivenessStateStore>());
        Assert.IsType<GroundworkWorkflowHoldStateStore>(provider.GetRequiredService<IWorkflowHoldStateStore>());
        Assert.IsType<GroundworkIncidentStateStore>(provider.GetRequiredService<IIncidentStateStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.Same(
            provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            provider.GetRequiredService<IRuntimePostCommitOutboxClaimStore>());
        Assert.Same(
            provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            provider.GetRequiredService<IRuntimePostCommitOutboxClaimCompletionStore>());
        Assert.IsType<GroundworkWorkflowDispatchStore>(provider.GetRequiredService<IWorkflowDispatchStore>());
        Assert.Same(provider.GetRequiredService<IWorkflowDispatchStore>(), provider.GetRequiredService<IWorkflowDispatchQueryStore>());
        Assert.Same(provider.GetRequiredService<IWorkflowDispatchStore>(), provider.GetRequiredService<IWorkflowDispatchDeleteStore>());
        Assert.Same(provider.GetRequiredService<IWorkflowDispatchStore>(), provider.GetRequiredService<IWorkflowDispatchRetentionRootStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<GroundworkWorkflowSchedulerPoisonStore>(provider.GetRequiredService<IWorkflowSchedulerPoisonStore>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GroundworkTestScopeProvider_ReplacesTheInMemoryDefaultInEitherCompositionOrder(bool runtimeFirst)
    {
        var services = new ServiceCollection();
        if (runtimeFirst)
        {
            services.AddWorkflowRuntime();
            services.AddGroundworkRuntimeStores();
        }
        else
        {
            services.AddGroundworkRuntimeStores();
            services.AddWorkflowRuntime();
        }

        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(WorkflowTestScopeProviderRegistration));
        var claim = Assert.IsType<WorkflowTestScopeProviderRegistration>(registration.ImplementationInstance);
        Assert.Equal(typeof(GroundworkWorkflowTestScopeStore), claim.ProviderType);
        Assert.False(claim.IsInMemoryDefault);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeAdmissionStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
    }

    // Constitution §2.23.1: the versioned-document serializer must be registered as the sealed default.
    [Fact]
    public void AddGroundworkRuntimeStores_Registers_Default_Serializer_Without_Consuming_Foreign_Upcasters()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        services.TryAddSingleton<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        services.AddSingleton<IDocumentJsonUpcaster>(new ForeignDocumentJsonUpcaster());

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkRuntimeDocumentSerializer>(provider.GetRequiredService<IGroundworkRuntimeDocumentSerializer>());
        Assert.IsType<ForeignDocumentJsonUpcaster>(Assert.Single(provider.GetServices<IDocumentJsonUpcaster>()));

    }

    private sealed class ForeignDocumentJsonUpcaster : IDocumentJsonUpcaster
    {
        public string DocumentKind => "foreign-document";
        public int FromVersion => 1;
        public JsonObject Upcast(JsonObject content) => content;
    }

    [Fact]
    public async Task Sqlite_Feature_Wires_DocumentStore_And_Bridge()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.AddWorkflowRuntime();

        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        // Startup admits the target and publishes a static provider session factory. Scoped adapters acquire
        // immutable access-bound stores from that source for each operation.
        Assert.NotNull(provider.GetRequiredService<GroundworkStoreSessionSource>());
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();

        Assert.IsType<GroundworkScopedDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkScopedDocumentStore>(provider.GetRequiredService<IBoundedDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<GroundworkWorkflowSchedulerPoisonStore>(provider.GetRequiredService<IWorkflowSchedulerPoisonStore>());

        var dispatchStore = provider.GetRequiredService<IWorkflowDispatchStore>();
        var dispatchQueryStore = provider.GetRequiredService<IWorkflowDispatchQueryStore>();
        var first = GroundworkWorkflowDispatchStoreTests.Pending("parent-physical", "activity-1");
        var second = GroundworkWorkflowDispatchStoreTests.Pending("parent-physical", "activity-2");
        await dispatchStore.SaveAsync(first);
        await dispatchStore.SaveAsync(second);
        await dispatchStore.SaveAsync(second.TransitionTo(WorkflowDispatchStatus.Started, DateTimeOffset.UnixEpoch.AddSeconds(1)));

        Assert.Equal(
            second.DispatchId,
            Assert.Single(await dispatchQueryStore.QueryAsync(new WorkflowDispatchQuery(
                parentWorkflowExecutionId: "parent-physical",
                status: WorkflowDispatchStatus.Started))).DispatchId);
    }

    [Fact] // Resolution is side-effect free before startup; the first provider operation is rejected until
           // startup admits the target, after which the same scoped adapter is usable.
    public async Task Sqlite_Store_Resolves_Before_Init_But_Rejects_IO_Until_Usable_After()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        // Before startup the scoped adapter resolves, but provider I/O is rejected because no admitted
        // session source has been published. Resolution never performs sync-over-async work.
        Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
        var uninitializedStore = provider.GetRequiredService<IDocumentStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => uninitializedStore.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            "not-open"));

        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();

        // After startup the scoped adapter acquires an initialized session and round-trips a document.
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-init", "1.0.0", "{\"ok\":true}"));
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-init"));
    }

    [Fact]
    public async Task Sqlite_Runtime_Admission_Fails_Pending_Without_Creating_Schema()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString, AutoApplySchemaOnStartup = false }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<GroundworkRuntimeSchemaAdmissionException>(
            () => provider.InitializeGroundworkStoreAsync());

        Assert.NotEmpty(exception.Result.PendingOperations);
        Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
        Assert.False(File.Exists(database.FilePath));
    }

    [Fact]
    public async Task Sqlite_Runtime_Initialization_Suppresses_Connection_Details()
    {
        const string secret = "sqlite-admission-secret";
        var connectionString = $"Data Source=:memory:;Unsupported={secret}";
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeGroundworkStoreAsync());

        Assert.Contains("connection details were suppressed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.ToString(), StringComparison.Ordinal);
        Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
    }

    [Fact] // Running startup twice is a no-op: the admitted provider factory is published exactly once.
    public async Task Sqlite_Store_Initialization_Is_Idempotent()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();
        var first = provider.GetRequiredService<GroundworkStoreSessionSource>();
        await provider.InitializeGroundworkStoreAsync();
        var second = provider.GetRequiredService<GroundworkStoreSessionSource>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Sqlite_Provider_Registration_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = "Data Source=registration.db" };

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStoreSessionSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBoundedDocumentStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(SqliteGroundworkDocumentStoreInitializer));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IShellInitializer));
    }

    [Fact]
    public void Sqlite_Provider_Prepare_Metadata_Matches_By_Type_After_Earlier_Initializers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IShellInitializer, EarlierInitializer>();

        new SqliteGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = "Data Source=registration.db"
        }.ConfigureServices(services);

        var registration = Assert.Single(
            services
                .Where(descriptor => descriptor.ServiceType == typeof(ShellInitializerRegistration))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<ShellInitializerRegistration>(),
            candidate => candidate.InitializerType == typeof(SqliteGroundworkDocumentStoreInitializer));

        Assert.Equal(LifecyclePhase.Prepare, registration.Phase);
        Assert.Equal(-1, registration.RegistrationIndex);
    }

    [Fact]
    public async Task Composed_Sqlite_Feature_Persists_Across_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-compose-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            // First host process: compose the feature exactly as a host would, then persist through a resolved seam.
            await using (var provider = await BuildComposedProviderAsync(connectionString))
            {
                var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
                await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
            }

            // Second host process: a fresh container over the same database file. State read back was genuinely durable.
            await using (var provider = await BuildComposedProviderAsync(connectionString))
            {
                var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
                Assert.NotNull(await bookmarks.FindAsync("wf-1", "bm-1"));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static async Task<ServiceProvider> BuildComposedProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await provider.ApplySqliteGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static BookmarkState Bookmark(string workflowExecutionId, string bookmarkId) => new(
        BookmarkId: bookmarkId,
        WorkflowExecutionId: workflowExecutionId,
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: "node-1",
        ResumeTargetId: "resume-1",
        StimulusType: "delivery-status",
        StimulusHash: "sha256:stimulus",
        Payload: null,
        Metadata: new Dictionary<string, string>(),
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: null);

    private static void AssertScopedWithinRequestButIsolatedAcrossRequests<TService>(
        IServiceScope firstRequest,
        IServiceScope secondRequest)
        where TService : class
    {
        var first = firstRequest.ServiceProvider.GetRequiredService<TService>();
        Assert.Same(first, firstRequest.ServiceProvider.GetRequiredService<TService>());
        Assert.NotSame(first, secondRequest.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-registration-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";
        public string FilePath => _path;

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EarlierInitializer : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
