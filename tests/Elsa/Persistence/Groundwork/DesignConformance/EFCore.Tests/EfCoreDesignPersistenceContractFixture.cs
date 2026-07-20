using System.Collections.Concurrent;
using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Events;
using Elsa.Persistence.EFCore.Handlers;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Design.Validations;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.DesignConformance.EFCore.Tests;

/// <summary>
/// Temporary SQLite-backed EF oracle. It intentionally exposes only the overlapping legacy behavior;
/// target scope, operation-ledger, and reusable-draft concurrency semantics remain outside this fixture.
/// </summary>
internal sealed class EfCoreDesignPersistenceContractFixture : IDesignPersistenceContractFixture
{
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-design-ef-oracle-{Guid.NewGuid():N}.db");
    private readonly EfAtomicityFaultController _faults = new();
    private readonly OracleEventPublisher _events = new();
    private IReadOnlyCollection<ActivityDefinitionVersion> _reconciliationCandidates = [];
    private ServiceProvider _services = null!;

    private EfCoreDesignPersistenceContractFixture()
    {
    }

    public string Provider => "legacy-ef-sqlite";

    public static async Task<EfCoreDesignPersistenceContractFixture> CreateAsync(CancellationToken cancellationToken = default)
    {
        var fixture = new EfCoreDesignPersistenceContractFixture();
        await fixture.OpenAsync(cancellationToken);
        return fixture;
    }

    public IServiceScope CreateScope(string storageScope)
    {
        if (!string.Equals(storageScope, DesignPersistenceFixtureData.ScopeA, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "The temporary EF oracle is deliberately limited to its LegacyEfOracle single-scope profile.");
        }

        return _services.CreateScope();
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _services.DisposeAsync();
        await OpenAsync(cancellationToken);
    }

    public async Task ValidateReadinessAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();
        var activities = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ActivitiesDesignDbContext>>();
        await using var workflowContext = await workflows.CreateDbContextAsync(cancellationToken);
        await using var activityContext = await activities.CreateDbContextAsync(cancellationToken);

        var pending = (await workflowContext.Database.GetPendingMigrationsAsync(cancellationToken))
            .Concat(await activityContext.Database.GetPendingMigrationsAsync(cancellationToken))
            .ToArray();
        if (pending.Length > 0)
            throw new InvalidOperationException($"The EF oracle database has pending migrations: {string.Join(", ", pending)}.");
    }

    public Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default)
    {
        EnsureScope(storageScope);
        cancellationToken.ThrowIfCancellationRequested();
        _reconciliationCandidates = candidates.ToArray();
        return Task.CompletedTask;
    }

    public void ClearObservedEvents() => _events.Clear();

    public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_events.Snapshot());
    }

    public Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
        DesignAtomicityFaultPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IDesignAtomicityFaultLease>(_faults.Arm(plan));
    }

    public async Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
        DesignAtomicityOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureScope(request.StorageScope);
        using var scope = _services.CreateScope();
        var services = scope.ServiceProvider;
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState());

        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            definition,
            draft,
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            cancellationToken);

        throw new InvalidOperationException(
            "The legacy EF oracle only executes injected partial-staging and cancellation attempts; it has no operation ledger for a successful canonical operation.");
    }

    public async Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
        string storageScope,
        CancellationToken cancellationToken = default)
    {
        EnsureScope(storageScope);
        await using var scope = _services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        var visible = 0;
        if (await context.WorkflowDefinitions.AnyAsync(x => x.Id == DesignPersistenceFixtureData.WorkflowDefinitionId, cancellationToken))
            visible++;
        if (await context.WorkflowDefinitionDrafts.AnyAsync(x => x.Id == DesignPersistenceFixtureData.WorkflowDraftId, cancellationToken))
            visible++;
        if (await context.WorkflowDefinitionDraftLayouts.AnyAsync(x => x.WorkflowDefinitionDraftId == DesignPersistenceFixtureData.WorkflowDraftId, cancellationToken))
            visible++;

        return new DesignAtomicitySnapshot(
            VisibleAggregatePartCount: visible,
            ExpectedAggregatePartCount: 3,
            DurableOutcomeCount: 0,
            PublishedOutcomeCount: 0,
            CanonicalAggregateStateFingerprint: null,
            AuthoritativeDurableResultFingerprint: null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // SQLite may retain an already-closing sidecar briefly; the uniquely named temporary file is harmless.
        }
    }

    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        _services = BuildServices();
        _events.ResetSubscriptions();
        WireEventHandlers(_services, _events);

        await using var scope = _services.CreateAsyncScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();
        var activities = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ActivitiesDesignDbContext>>();
        await using var workflowContext = await workflows.CreateDbContextAsync(cancellationToken);
        await using var activityContext = await activities.CreateDbContextAsync(cancellationToken);
        await workflowContext.Database.MigrateAsync(cancellationToken);
        await activityContext.Database.MigrateAsync(cancellationToken);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IDistributedLockProvider, ImmediateDistributedLockProvider>();
        services.AddSingleton<IInlineEventPublisher>(_events);
        services.AddSingleton<IDeferredEventPublisher>(_events);
        services.AddSingleton<EfAtomicityFaultController>(_faults);
        services.AddSingleton<SaveChangesInterceptor>(_faults.Interceptor);
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();

        new OracleWorkflowsFeature(ConnectionString, _faults.Interceptor).ConfigureServices(services);
        new OracleActivitiesFeature(ConnectionString, _faults.Interceptor).ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private void WireEventHandlers(ServiceProvider services, OracleEventPublisher publisher)
    {
        publisher.Subscribe<OnEntitySaving>(async @event =>
        {
            await using var scope = services.CreateAsyncScope();
            await new ApplyEntitySavingHandlers(scope.ServiceProvider).Handle(@event, CancellationToken.None);
        });
        publisher.Subscribe<OnEntityLoading>(async @event =>
        {
            await using var scope = services.CreateAsyncScope();
            await new ApplyEntityLoadingHandlers(scope.ServiceProvider).Handle(@event, CancellationToken.None);
        });
        publisher.Subscribe<Elsa.Workflows.Design.Validations.Core.Events.OnDraftValidating>(async @event =>
        {
            await using var scope = services.CreateAsyncScope();
            foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler<Elsa.Workflows.Design.Validations.Core.Events.OnDraftValidating>>())
                await handler.Handle(@event, CancellationToken.None);
        });
        publisher.Subscribe<OnActivityVersionsReconciling>(@event =>
        {
            foreach (var candidate in _reconciliationCandidates)
                @event.Versions.Add(candidate);
            return Task.CompletedTask;
        });
    }

    private string ConnectionString => $"Data Source={_databasePath}";

    private static void EnsureScope(string storageScope)
    {
        if (!string.Equals(storageScope, DesignPersistenceFixtureData.ScopeA, StringComparison.Ordinal))
            throw new NotSupportedException("The temporary EF oracle does not implement target storage-scope semantics.");
    }

    private sealed class OracleWorkflowsFeature : SqliteWorkflowsDesignPersistenceShellFeature
    {
        public OracleWorkflowsFeature(string connectionString, SaveChangesInterceptor interceptor)
        {
            ConnectionString = connectionString;
            RunMigrations = false;
            DbContextOptionsBuilder = (_, builder) => builder.AddInterceptors(interceptor);
        }

        protected override Assembly GetMigrationsAssembly() => typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly;
    }

    private sealed class OracleActivitiesFeature : SqliteActivitiesDesignPersistenceShellFeature
    {
        public OracleActivitiesFeature(string connectionString, SaveChangesInterceptor interceptor)
        {
            ConnectionString = connectionString;
            RunMigrations = false;
            DbContextOptionsBuilder = (_, builder) => builder.AddInterceptors(interceptor);
        }

        protected override Assembly GetMigrationsAssembly() => typeof(SqliteActivitiesDesignPersistenceShellFeature).Assembly;
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;

        public string Generate() => $"oracle-{Interlocked.Increment(ref _next):D4}";
    }

}

internal sealed class EfAtomicityFaultController
{
    private EfAtomicityFaultLease? _armed;

    public SaveChangesInterceptor Interceptor { get; }

    public EfAtomicityFaultController() => Interceptor = new FaultInterceptor(this);

    public IDesignAtomicityFaultLease Arm(DesignAtomicityFaultPlan plan)
    {
        if (plan is not
            { Phase: DesignAtomicityFaultPhase.AfterStagedWrite, Action: DesignAtomicityFaultAction.Throw } and not
            { Phase: DesignAtomicityFaultPhase.BeforeProviderDecision, Action: DesignAtomicityFaultAction.Cancel })
        {
            throw new NotSupportedException("The temporary EF oracle supports only its two LegacyEfOracle atomicity probes.");
        }

        if (_armed is not null)
            throw new InvalidOperationException("Only one EF atomicity fault may be armed at a time.");

        _armed = new EfAtomicityFaultLease(this, plan);
        return _armed;
    }

    private void Trigger()
    {
        var lease = _armed;
        if (lease is null)
            return;

        lease.Trigger();
        if (lease.Plan.Action == DesignAtomicityFaultAction.Cancel)
        {
            lease.Cancel();
            throw new OperationCanceledException("Injected EF oracle cancellation before provider decision.", null, lease.Token);
        }

        throw new InvalidOperationException("Injected EF oracle failure after staging the aggregate.");
    }

    private void Disarm(EfAtomicityFaultLease lease)
    {
        if (ReferenceEquals(_armed, lease))
            _armed = null;
    }

    private sealed class FaultInterceptor(EfAtomicityFaultController controller) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is WorkflowsDesignDbContext)
                controller.Trigger();

            return ValueTask.FromResult(result);
        }
    }

    private sealed class EfAtomicityFaultLease(EfAtomicityFaultController owner, DesignAtomicityFaultPlan plan)
        : IDesignAtomicityFaultLease
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;

        public DesignAtomicityFaultPlan Plan { get; } = plan;
        public bool WasTriggered { get; private set; }
        public CancellationToken Token => _cancellation.Token;

        public void Trigger() => WasTriggered = true;
        public void Cancel() => _cancellation.Cancel();

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Disarm(this);
                _cancellation.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class OracleEventPublisher : IInlineEventPublisher, IDeferredEventPublisher
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly List<(Type EventType, Func<IEvent, Task> Handler)> _subscriptions = [];

    public void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent =>
        _subscriptions.Add((typeof(T), @event => handler((T)@event)));

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }

    public void ResetSubscriptions() => _subscriptions.Clear();

    public IReadOnlyList<IEvent> Snapshot() => _events.ToArray();

    Task IInlineEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) => PublishAsync(@event, cancellationToken);
    Task IDeferredEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) => PublishAsync(@event, cancellationToken);

    private async Task PublishAsync(IEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(@event);
        foreach (var subscription in _subscriptions.Where(subscription => subscription.EventType.IsInstanceOfType(@event)))
            await subscription.Handler(@event);
    }
}

internal sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
