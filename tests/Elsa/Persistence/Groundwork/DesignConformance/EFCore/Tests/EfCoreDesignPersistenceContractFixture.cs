using System.Collections.Concurrent;
using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Events;
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
    private readonly OracleEventCapture _events = new();
    private ServiceProvider _services = null!;

    private EfCoreDesignPersistenceContractFixture()
    {
    }

    public string Provider => "legacy-ef-sqlite";
    internal EfCancellationProbeEvidence? CancellationProbeEvidence => _faults.CancellationProbeEvidence;

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
        _events.StageCandidates(candidates);
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
        using var operationCancellation = _faults.BeginOperation(cancellationToken);

        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey($"ef-oracle-atomicity:{request.OperationKey.Value}"),
            definition,
            draft,
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            operationCancellation.Token);

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
            PostCommitOutcomeCount: 0,
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
        services.AddSingleton<IDistributedLockProvider, ImmediateDesignContractLockProvider>();
        services.AddSingleton<EfAtomicityFaultController>(_faults);
        services.AddSingleton<SaveChangesInterceptor>(_faults.Interceptor);
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();
        new EventsFeature().ConfigureServices(services);
        services.AddScoped<IDeferredEventPublisher>(sp =>
            new OracleDeferredEventPublisher(sp.GetRequiredService<IEventPublisher>(), _events));

        new OracleWorkflowsFeature(ConnectionString, _faults.Interceptor).ConfigureServices(services);
        new OracleActivitiesFeature(ConnectionString, _faults.Interceptor).ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        services.AddScoped<IEventHandler<OnActivityVersionsReconciling>>(
            _ => new OracleActivityReconciliationHandler(_events));
        services.AddScoped<IEventHandler<OnEntitySaving>>(
            _ => new OracleCancellationTokenProbeHandler(_faults));

        return services.BuildServiceProvider(validateScopes: true);
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
    private CancellationToken? _eventHandlerToken;

    public SaveChangesInterceptor Interceptor { get; }
    public EfCancellationProbeEvidence? CancellationProbeEvidence { get; private set; }

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
        _eventHandlerToken = null;
        CancellationProbeEvidence = null;
        return _armed;
    }

    public void ObserveEventHandlerToken(CancellationToken cancellationToken)
    {
        if (_armed is { Plan.Action: DesignAtomicityFaultAction.Cancel })
            _eventHandlerToken = cancellationToken;
    }

    public EfAtomicityOperationCancellation BeginOperation(CancellationToken callerToken)
    {
        var lease = _armed;
        return lease is { Plan.Action: DesignAtomicityFaultAction.Cancel }
            ? lease.BeginCancellationOperation(callerToken)
            : EfAtomicityOperationCancellation.PassThrough(callerToken);
    }

    private void Trigger(CancellationToken providerToken)
    {
        var lease = _armed;
        if (lease is null)
            return;

        lease.Trigger();
        if (lease.Plan.Action == DesignAtomicityFaultAction.Cancel)
        {
            if (_eventHandlerToken is not { } eventHandlerToken || eventHandlerToken != lease.OperationToken)
            {
                throw new InvalidOperationException(
                    "The Elsa event pipeline did not carry the public command cancellation token to its handlers.");
            }

            if (providerToken != lease.OperationToken)
            {
                throw new InvalidOperationException(
                    "EF did not receive the linked cancellation token supplied to the public workflow command.");
            }

            lease.Cancel();
            CancellationProbeEvidence = new(lease.OperationToken, eventHandlerToken, providerToken);
            providerToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The injected EF cancellation token was not observed as cancelled.");
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
                controller.Trigger(cancellationToken);

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
        public CancellationToken OperationToken { get; private set; }

        public void Trigger() => WasTriggered = true;
        public void Cancel() => _cancellation.Cancel();

        public EfAtomicityOperationCancellation BeginCancellationOperation(CancellationToken callerToken)
        {
            if (OperationToken.CanBeCanceled)
                throw new InvalidOperationException("The armed EF cancellation fault is already bound to an operation.");

            var source = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _cancellation.Token);
            OperationToken = source.Token;
            return new EfAtomicityOperationCancellation(source);
        }

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

internal sealed class EfAtomicityOperationCancellation : IDisposable
{
    private readonly CancellationTokenSource? _source;

    public EfAtomicityOperationCancellation(CancellationTokenSource source)
    {
        _source = source;
        Token = source.Token;
    }

    private EfAtomicityOperationCancellation(CancellationToken token) => Token = token;

    public CancellationToken Token { get; }

    public static EfAtomicityOperationCancellation PassThrough(CancellationToken token) => new(token);

    public void Dispose() => _source?.Dispose();
}

internal sealed record EfCancellationProbeEvidence(
    CancellationToken CommandToken,
    CancellationToken EventHandlerToken,
    CancellationToken ProviderToken);

internal sealed class OracleEventCapture
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private IReadOnlyCollection<ActivityDefinitionVersion> _reconciliationCandidates = [];

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<IEvent> Snapshot() => _events.ToArray();

    public void Record(IEvent @event) => _events.Enqueue(@event);

    public void StageCandidates(IReadOnlyCollection<ActivityDefinitionVersion> candidates) =>
        _reconciliationCandidates = candidates.ToArray();

    public IReadOnlyCollection<ActivityDefinitionVersion> Candidates => _reconciliationCandidates;
}

internal sealed class OracleDeferredEventPublisher(
    IEventPublisher eventPublisher,
    OracleEventCapture capture) : IDeferredEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        return eventPublisher.Publish(@event, EventPublishingStrategy.Background, cancellationToken);
    }
}

internal sealed class OracleActivityReconciliationHandler(OracleEventCapture capture)
    : IEventHandler<OnActivityVersionsReconciling>
{
    public Task Handle(OnActivityVersionsReconciling @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        foreach (var candidate in capture.Candidates)
            @event.Versions.Add(candidate);
        return Task.CompletedTask;
    }
}

internal sealed class OracleCancellationTokenProbeHandler(EfAtomicityFaultController controller)
    : IEventHandler<OnEntitySaving>
{
    public Task Handle(OnEntitySaving @event, CancellationToken cancellationToken)
    {
        controller.ObserveEventHandlerToken(cancellationToken);
        return Task.CompletedTask;
    }
}
