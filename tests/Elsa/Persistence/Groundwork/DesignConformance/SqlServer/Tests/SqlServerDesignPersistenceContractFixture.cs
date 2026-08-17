using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Events;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

internal sealed class SqlServerDesignPersistenceContractFixture : IDesignPersistenceContractFixture
{
    private readonly GroundworkBaselineTelemetry _telemetry;
    private readonly GroundworkTargetEventCapture _events;
    private readonly GroundworkTargetAtomicityFaultController _atomicityFaults = new();
    // This is a fixture-local continuation observation for the raw-document atomicity probe. It
    // intentionally does not stand in for an IEvent or a workflow lifecycle publication.
    private readonly ConcurrentDictionary<string, byte> _postCommitAtomicOutcomes = new(StringComparer.Ordinal);
    private readonly string _connectionString;
    private ServiceProvider _services = null!;
    private CancellationTokenSource? _backgroundEventCancellation;
    private IReadOnlyList<IBackgroundTask> _backgroundEventTasks = [];
    private Task[] _backgroundEventExecutions = [];

    private SqlServerDesignPersistenceContractFixture(GroundworkBaselineTelemetry telemetry, string connectionString)
    {
        _telemetry = telemetry;
        _connectionString = connectionString;
        _events = new(telemetry);
    }

    public string Provider => "groundwork-sqlserver";
    public int RestartCount { get; private set; }
    public int BoundScopeCount { get; private set; }
    public GroundworkSchemaEvidence SchemaEvidence { get; private set; } = null!;

    /// <summary>The applied target database's connection string, for leaf-local plan capture.</summary>
    internal string SqlServerConnectionString => _connectionString;

    public static async Task<SqlServerDesignPersistenceContractFixture> CreateAsync(
        SqlServerDesignProviderFixture container,
        GroundworkBaselineTelemetry telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!container.IsAvailable)
            throw new InvalidOperationException(container.SkipReason ?? "The SQL Server design-conformance container is unavailable.");

        var connectionString = await container.CreateDesignDatabaseAsync(cancellationToken);
        var fixture = new SqlServerDesignPersistenceContractFixture(telemetry, connectionString);
        fixture.SchemaEvidence = await GroundworkSchemaCli.ApplyFreshAsync(connectionString, cancellationToken);
        telemetry.RecordSchema(fixture.SchemaEvidence);
        await fixture.OpenAndAdmitAsync(cancellationToken);
        return fixture;
    }

    public IServiceScope CreateScope(string storageScope)
    {
        var scope = _services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(storageScope)));
            BoundScopeCount++;
            _telemetry.RecordBoundScope();
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopBackgroundEventsAsync(cancellationToken);
        await _services.DisposeAsync();
        RestartCount++;
        _telemetry.RecordRestart();
        await OpenAndAdmitAsync(cancellationToken);
    }

    public async Task ValidateReadinessAsync(CancellationToken cancellationToken = default)
    {
        var validation = await GroundworkSchemaCli.RunAsync(
            "validate",
            _connectionString,
            cancellationToken);
        if (validation.ExitCode != 0)
            throw validation.ToException("Groundwork live schema validation failed");
        if (!string.Equals(validation.Report.GetProperty("outcome").GetString(), "ready", StringComparison.OrdinalIgnoreCase))
            throw validation.ToException("Groundwork live schema validation did not report ready");
    }

    public Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Stage(storageScope, candidates);
        _telemetry.RecordCandidates(candidates.Count);
        return Task.CompletedTask;
    }

    public void ClearObservedEvents() => _events.Clear();

    public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_events.Snapshot());
    }

    internal Task<DraftCreated> WaitForPublishedDraftCreatedAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _events.WaitForPublishedDraftCreatedAsync(draftId, cancellationToken);
    }

    public Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
        DesignAtomicityFaultPlan plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IDesignAtomicityFaultLease>(_atomicityFaults.Arm(plan, cancellationToken));

    public async Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
        DesignAtomicityOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = CreateScope(request.StorageScope);
        var atomicWrite = scope.ServiceProvider.GetRequiredService<IDesignAtomicWriter>();
        var identities = AtomicityDocumentIdentities.Create(request.StorageScope, request.OperationKey.Value);
        using var operationCancellation = _atomicityFaults.BeginOperation(cancellationToken);
        var atomicRequest = new GroundworkDesignAtomicWriteRequest(
            new GroundworkDesignOperationIdentity(
                AtomicityOperationKind,
                request.OperationKey.Value),
            request.CanonicalRequestFingerprint.Value,
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind
            ]);

        var result = await atomicWrite.ExecuteAsync(
            atomicRequest,
            async (context, token) =>
            {
                await context.SaveAsync(DefinitionSave(identities, request.StorageScope), token);
                _atomicityFaults.ThrowIfTriggered(
                    DesignAtomicityFaultPhase.AfterStagedWrite,
                    operationCancellation);
                await context.SaveAsync(VersionSave(identities, request.StorageScope), token);

                return _atomicityFaults.ResolveProviderDecision(operationCancellation, token)
                    ? GroundworkDesignAtomicWriteStageResult.Rejected()
                    : GroundworkDesignAtomicWriteStageResult.Accepted(
                        ResultFingerprint(request.StorageScope, request.CanonicalRequestFingerprint.Value, identities),
                        ResultJson(request.StorageScope, request.CanonicalRequestFingerprint.Value, identities));
            },
            operationCancellation.Token);

        var mapped = MapAtomicityResult(result);
        if (mapped.Status is DesignAtomicityOperationStatus.Committed)
            _postCommitAtomicOutcomes.TryAdd(OutcomeKey(request.StorageScope, request.OperationKey.Value), 0);

        _atomicityFaults.ThrowIfTriggered(
            DesignAtomicityFaultPhase.AfterDurableDecision,
            operationCancellation);
        return mapped;
    }

    public async Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
        string storageScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageScope);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = CreateScope(storageScope);
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var identities = AtomicityDocumentIdentities.Create(storageScope, AtomicitySnapshotOperationKey);
        var definition = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            identities.DefinitionId,
            cancellationToken);
        var version = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            identities.VersionId,
            cancellationToken);
        var marker = await store.LoadAsync(
            GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
            MarkerId(AtomicitySnapshotOperationKey),
            cancellationToken);
        var visibleParts = new[] { definition, version }.Count(x => x is not null);
        var markerResultFingerprint = marker is null ? null : MarkerResultFingerprint(marker.ContentJson);

        return new DesignAtomicitySnapshot(
            VisibleAggregatePartCount: visibleParts,
            ExpectedAggregatePartCount: 2,
            DurableOutcomeCount: marker is null ? 0 : 1,
            PostCommitOutcomeCount: marker is not null && _postCommitAtomicOutcomes.ContainsKey(
                OutcomeKey(storageScope, AtomicitySnapshotOperationKey)) ? 1 : 0,
            CanonicalAggregateStateFingerprint: definition is not null && version is not null
                ? Digest($"{definition.ContentJson}\n{version.ContentJson}")
                : null,
            AuthoritativeDurableResultFingerprint: markerResultFingerprint);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await StopBackgroundEventsAsync(CancellationToken.None);
            await _services.DisposeAsync();
        }
        // The per-fixture database is discarded with the shared container; dropping it here is optional.
    }

    private async Task OpenAndAdmitAsync(CancellationToken cancellationToken)
    {
        _services = BuildServices();
        await LegacyGroundworkTestHost.InitializeAsync(_services, cancellationToken);

        _backgroundEventCancellation = new CancellationTokenSource();
        _backgroundEventTasks = _services.GetServices<IBackgroundTask>().ToArray();
        foreach (var task in _backgroundEventTasks)
            await task.StartAsync(_backgroundEventCancellation.Token);
        _backgroundEventExecutions = _backgroundEventTasks
            .Select(task => task.ExecuteAsync(_backgroundEventCancellation.Token))
            .ToArray();
    }

    private async Task StopBackgroundEventsAsync(CancellationToken cancellationToken)
    {
        if (_backgroundEventCancellation is null)
            return;

        try
        {
            foreach (var task in _backgroundEventTasks)
                await task.StopAsync(cancellationToken);
            await Task.WhenAll(_backgroundEventExecutions);
        }
        finally
        {
            _backgroundEventCancellation.Cancel();
            _backgroundEventCancellation.Dispose();
            _backgroundEventCancellation = null;
            _backgroundEventTasks = [];
            _backgroundEventExecutions = [];
        }
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(_events);
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IDistributedLockProvider, ImmediateDesignContractLockProvider>();
        services.AddSingleton<IPayloadSerializer, DesignPersistenceFixtureData.DeterministicPayloadSerializer>();
        new EventsFeature().ConfigureServices(services);
        services.AddScoped<IDeferredEventPublisher>(sp =>
            new GroundworkTargetDeferredEventPublisher(
                sp.GetRequiredService<IEventPublisher>(),
                _events));
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddDesignPersistencePublicationDeletionGuard();
        services.AddGroundworkSqlServerUnifiedPersistence(_connectionString, autoApplyOnStartup: false);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        services.AddScoped<IEventHandler<GroundworkStorageComposing>>(
            _ => new GroundworkTargetCaptureHandler<GroundworkStorageComposing>(_events));
        services.AddScoped<IEventHandler<DraftValidating>>(
            _ => new GroundworkTargetCaptureHandler<DraftValidating>(_events));
        services.AddScoped<IEventHandler<DraftCreated>, GroundworkTargetDraftCreatedCaptureHandler>();
        services.AddScoped<IEventHandler<ActivityVersionsReconciling>>(sp =>
            new GroundworkTargetReconciliationHandler(
                sp.GetRequiredService<IPersistenceAccessContextAccessor>(),
                _events));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SaveDocumentRequest DefinitionSave(AtomicityDocumentIdentities identities, string storageScope)
    {
        var definition = new WorkflowDefinition
        {
            Id = identities.DefinitionId,
            TenantId = storageScope,
            Name = $"Atomicity probe {identities.Fingerprint}",
            Description = "SQL Server design-conformance atomicity probe",
            CreatedAt = DesignPersistenceFixtureData.Epoch,
            LastModifiedAt = DesignPersistenceFixtureData.Epoch
        };
        return GroundworkDocumentWriter.ToSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkDesignJson.Options) with
        { ExpectedVersion = 0 };
    }

    private static SaveDocumentRequest VersionSave(AtomicityDocumentIdentities identities, string storageScope)
    {
        var version = new WorkflowDefinitionVersion(identities.DefinitionId, "1.0.0")
        {
            Id = identities.VersionId,
            TenantId = storageScope,
            CreatedAt = DesignPersistenceFixtureData.Epoch,
            LastModifiedAt = DesignPersistenceFixtureData.Epoch,
            SourceCreatedAt = DesignPersistenceFixtureData.Epoch
        };
        return GroundworkDocumentWriter.ToSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            version,
            GroundworkDesignJson.Options) with
        { ExpectedVersion = 0 };
    }

    private static DesignAtomicityOperationResult MapAtomicityResult(GroundworkDesignAtomicWriteResult result) =>
        result.Status switch
        {
            GroundworkDesignAtomicWriteStatus.Committed => new(
                DesignAtomicityOperationStatus.Committed,
                result.AuthoritativeResultFingerprint),
            GroundworkDesignAtomicWriteStatus.Reconciled => new(
                DesignAtomicityOperationStatus.Committed,
                result.AuthoritativeResultFingerprint),
            GroundworkDesignAtomicWriteStatus.Replayed => new(
                DesignAtomicityOperationStatus.Replayed,
                result.AuthoritativeResultFingerprint),
            GroundworkDesignAtomicWriteStatus.Rejected => new(
                DesignAtomicityOperationStatus.Rejected,
                null),
            GroundworkDesignAtomicWriteStatus.Conflict => new(
                DesignAtomicityOperationStatus.Conflict,
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    private static string OutcomeKey(string storageScope, string operationKey) => $"{storageScope}\n{operationKey}";

    private static string ResultFingerprint(
        string storageScope,
        string requestFingerprint,
        AtomicityDocumentIdentities identities) =>
        Digest($"{storageScope}\n{requestFingerprint}\n{identities.DefinitionId}\n{identities.VersionId}");

    private static string ResultJson(
        string storageScope,
        string requestFingerprint,
        AtomicityDocumentIdentities identities) =>
        JsonSerializer.Serialize(new
        {
            storageScope,
            requestFingerprint,
            identities.DefinitionId,
            identities.VersionId
        });

    private static string MarkerId(string operationKey)
    {
        var framed = string.Concat(
            "elsa-design-operation:v1|",
            Encoding.UTF8.GetByteCount(AtomicityOperationKind), ":", AtomicityOperationKind, "|",
            Encoding.UTF8.GetByteCount(operationKey), ":", operationKey);
        return $"design-operation-v1-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(framed)))}";
    }

    private static string? MarkerResultFingerprint(string contentJson)
    {
        using var marker = JsonDocument.Parse(contentJson);
        return marker.RootElement.TryGetProperty("authoritativeResultFingerprint", out var value)
            ? value.GetString()
            : null;
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private const string AtomicityOperationKind = "design-conformance.atomicity.v1";
    private const string AtomicitySnapshotOperationKey = "design-atomicity-create-v1";

    private sealed record AtomicityDocumentIdentities(string Fingerprint, string DefinitionId, string VersionId)
    {
        public static AtomicityDocumentIdentities Create(string storageScope, string operationKey)
        {
            var fingerprint = Digest($"{storageScope}\n{operationKey}")[..24];
            return new(fingerprint, $"atomicity-definition-{fingerprint}", $"atomicity-version-{fingerprint}");
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"groundwork-{Interlocked.Increment(ref _next):D6}";
    }

    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}

internal sealed class GroundworkTargetAtomicityFaultController
{
    private GroundworkTargetAtomicityFaultLease? _armed;

    public IDesignAtomicityFaultLease Arm(DesignAtomicityFaultPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (_armed is not null)
            throw new InvalidOperationException("Only one SQL Server atomicity fault may be armed at a time.");

        _armed = new GroundworkTargetAtomicityFaultLease(this, plan);
        return _armed;
    }

    public GroundworkTargetAtomicityOperationCancellation BeginOperation(CancellationToken callerToken) =>
        _armed?.BeginOperation(callerToken) ?? GroundworkTargetAtomicityOperationCancellation.PassThrough(callerToken);

    public void ThrowIfTriggered(
        DesignAtomicityFaultPhase phase,
        GroundworkTargetAtomicityOperationCancellation operation)
    {
        var fault = _armed;
        if (fault is null || fault.Plan.Phase != phase)
            return;

        if (!fault.TryTrigger())
            return;

        switch (fault.Plan.Action)
        {
            case DesignAtomicityFaultAction.Throw:
                throw new InvalidOperationException($"Injected SQL Server atomicity fault at '{phase}'.");
            case DesignAtomicityFaultAction.Cancel:
                operation.Cancel();
                operation.Token.ThrowIfCancellationRequested();
                break;
            case DesignAtomicityFaultAction.ReturnNonSuccess:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault.Plan), fault.Plan.Action, null);
        }
    }

    public bool ResolveProviderDecision(
        GroundworkTargetAtomicityOperationCancellation operation,
        CancellationToken cancellationToken)
    {
        var fault = _armed;
        if (fault is null || fault.Plan.Phase != DesignAtomicityFaultPhase.BeforeProviderDecision)
            return false;

        if (!fault.TryTrigger())
            return false;

        switch (fault.Plan.Action)
        {
            case DesignAtomicityFaultAction.ReturnNonSuccess:
                return true;
            case DesignAtomicityFaultAction.Cancel:
                operation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            case DesignAtomicityFaultAction.Throw:
                throw new InvalidOperationException("Injected SQL Server atomicity provider-decision fault.");
            default:
                throw new ArgumentOutOfRangeException(nameof(fault.Plan), fault.Plan.Action, null);
        }
    }

    private void Disarm(GroundworkTargetAtomicityFaultLease lease)
    {
        if (ReferenceEquals(_armed, lease))
            _armed = null;
    }

    private sealed class GroundworkTargetAtomicityFaultLease(
        GroundworkTargetAtomicityFaultController owner,
        DesignAtomicityFaultPlan plan) : IDesignAtomicityFaultLease
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;

        public DesignAtomicityFaultPlan Plan { get; } = plan;
        public bool WasTriggered { get; private set; }

        public bool TryTrigger()
        {
            if (WasTriggered)
                return false;

            WasTriggered = true;
            return true;
        }

        public GroundworkTargetAtomicityOperationCancellation BeginOperation(CancellationToken callerToken) =>
            new(CancellationTokenSource.CreateLinkedTokenSource(callerToken, _cancellation.Token), _cancellation);

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

internal sealed class GroundworkTargetAtomicityOperationCancellation : IDisposable
{
    private readonly CancellationTokenSource? _source;
    private readonly CancellationTokenSource? _faultCancellation;

    public GroundworkTargetAtomicityOperationCancellation(
        CancellationTokenSource source,
        CancellationTokenSource faultCancellation)
    {
        _source = source;
        _faultCancellation = faultCancellation;
        Token = source.Token;
    }

    private GroundworkTargetAtomicityOperationCancellation(CancellationToken token) => Token = token;

    public CancellationToken Token { get; }

    public static GroundworkTargetAtomicityOperationCancellation PassThrough(CancellationToken token) => new(token);
    public void Cancel() => _faultCancellation?.Cancel();
    public void Dispose() => _source?.Dispose();
}

internal sealed class GroundworkTargetEventCapture(GroundworkBaselineTelemetry telemetry)
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly ConcurrentQueue<DraftCreated> _publishedDraftCreatedEvents = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DraftCreated>> _publishedDraftCreatedWaiters =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<ActivityDefinitionVersion>> _candidates =
        new(StringComparer.Ordinal);

    public void Stage(string scope, IReadOnlyCollection<ActivityDefinitionVersion> candidates) =>
        _candidates[scope] = candidates.ToArray();

    public IReadOnlyCollection<ActivityDefinitionVersion> Candidates(string scope) =>
        _candidates.TryGetValue(scope, out var candidates) ? candidates : [];

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }

        while (_publishedDraftCreatedEvents.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<IEvent> Snapshot() => _events.ToArray();
    public void Record(IEvent @event)
    {
        _events.Enqueue(@event);
        telemetry.RecordEvent(@event.GetType());
    }

    public void RecordReconciliationPass() => telemetry.RecordReconciliationPass();
    public Task<DraftCreated> WaitForPublishedDraftCreatedAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var waiter = _publishedDraftCreatedWaiters
            .GetOrAdd(draftId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        var observed = _publishedDraftCreatedEvents.FirstOrDefault(@event => @event.DraftId == draftId);
        if (observed is not null)
        {
            _publishedDraftCreatedWaiters.TryRemove(draftId, out _);
            waiter.TrySetResult(observed);
        }

        return waiter.Task.WaitAsync(cancellationToken);
    }

    public void RecordPublishedDraftCreated(DraftCreated @event)
    {
        _publishedDraftCreatedEvents.Enqueue(@event);
        if (_publishedDraftCreatedWaiters.TryRemove(@event.DraftId, out var waiter))
            waiter.TrySetResult(@event);
    }
}

internal sealed class GroundworkTargetDeferredEventPublisher(
    IEventPublisher eventPublisher,
    GroundworkTargetEventCapture capture)
    : IDeferredEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        return eventPublisher.Publish(@event, EventPublishingStrategy.Background, cancellationToken);
    }
}

internal sealed class GroundworkTargetCaptureHandler<TEvent>(GroundworkTargetEventCapture capture)
    : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task Handle(TEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Observes the real, background-dispatched lifecycle-event pipeline only after the public
/// draft store can read the created draft. This is deliberately separate from the raw-document
/// atomicity probe and its fixture-local post-commit observation.
/// </summary>
internal sealed class GroundworkTargetDraftCreatedCaptureHandler(
    IPersistenceAccessContextBinder accessContextBinder,
    IWorkflowDefinitionDraftStore drafts,
    GroundworkTargetEventCapture capture)
    : IEventHandler<DraftCreated>
{
    public async Task Handle(DraftCreated @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // DraftCreated intentionally carries aggregate identity rather than a persistence scope.
        // The SQL Server lifecycle evidence exercises ScopeA, so the background handler binds that
        // explicit test scope before using the same public read port as an application consumer.
        accessContextBinder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope(DesignPersistenceFixtureData.ScopeA)));
        if (await drafts.FindWithLayoutByIdAsync(@event.DraftId, cancellationToken) is null)
        {
            throw new InvalidOperationException(
                "DraftCreated reached the composed event pipeline before its draft was durable.");
        }

        capture.RecordPublishedDraftCreated(@event);
    }
}

internal sealed class GroundworkTargetReconciliationHandler(
    IPersistenceAccessContextAccessor accessContext,
    GroundworkTargetEventCapture capture)
    : IEventHandler<ActivityVersionsReconciling>
{
    public Task Handle(ActivityVersionsReconciling @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        capture.RecordReconciliationPass();
        var storageScope = accessContext.Current.Scope?.Value
                           ?? throw new InvalidOperationException(
                               "Activity reconciliation requires a scope-bound persistence access context.");
        foreach (var candidate in capture.Candidates(storageScope))
            @event.Versions.Add(candidate);
        return Task.CompletedTask;
    }
}

internal sealed record GroundworkSchemaEvidence(string TargetFingerprint, string PlanFingerprint);

internal sealed class GroundworkBaselineTelemetry
{
    private readonly object _sync = new();
    private readonly List<string> _eventTypes = [];
    private GroundworkSchemaEvidence? _schema;
    private int _restartCount;
    private int _boundScopeCount;
    private int _candidateCount;
    private int _reconciliationPassCount;

    public void RecordSchema(GroundworkSchemaEvidence schema)
    {
        lock (_sync)
        {
            if (_schema is not null && _schema != schema)
                throw new InvalidOperationException("Groundwork schema fingerprints drifted within one baseline run.");
            _schema = schema;
        }
    }

    public void RecordRestart() => Interlocked.Increment(ref _restartCount);
    public void RecordBoundScope() => Interlocked.Increment(ref _boundScopeCount);
    public void RecordCandidates(int count) => Interlocked.Add(ref _candidateCount, count);
    public void RecordReconciliationPass() => Interlocked.Increment(ref _reconciliationPassCount);

    public void RecordEvent(Type eventType)
    {
        lock (_sync)
            _eventTypes.Add(eventType.FullName ?? eventType.Name);
    }
}

internal static class GroundworkSchemaCli
{
    private const string ConnectionEnvironmentVariable = "ELSA_DESIGN_GROUNDWORK_SQLSERVER_CONNECTION";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    public static async Task<GroundworkSchemaEvidence> ApplyFreshAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var offline = await RunAsync("validate", null, cancellationToken, "--offline");
        if (offline.ExitCode != 0)
            throw offline.ToException("Groundwork offline schema validation failed");

        var plan = await RunAsync("plan", connectionString, cancellationToken);
        if (plan.ExitCode != 2)
            throw plan.ToException("A fresh Groundwork SQL Server target did not report a pending plan");

        var apply = await RunAsync("apply", connectionString, cancellationToken, "--safe");
        if (apply.ExitCode != 0)
            throw apply.ToException("Groundwork safe schema apply failed");

        return new(
            apply.Report.GetProperty("target").GetProperty("fingerprint").GetString()
            ?? throw new InvalidOperationException("Groundwork apply omitted the target fingerprint."),
            apply.Report.GetProperty("planFingerprint").GetString()
            ?? plan.Report.GetProperty("planFingerprint").GetString()
            ?? throw new InvalidOperationException("Groundwork apply omitted the plan fingerprint."));
    }

    public static async Task<GroundworkCliResult> RunAsync(
        string command,
        string? connectionString,
        CancellationToken cancellationToken,
        params string[] extraArguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", typeof(GroundworkAllFeaturesDeploymentSchema).Assembly.Location,
                     "--manifest-type", typeof(GroundworkAllFeaturesDeploymentSchema).FullName!,
                     "--provider", "sqlserver",
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        if (connectionString is not null)
        {
            start.Environment[ConnectionEnvironmentVariable] = connectionString;
            start.ArgumentList.Add("--connection-env");
            start.ArgumentList.Add(ConnectionEnvironmentVariable);
        }
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Could not start the Groundwork schema tool.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            try
            {
                using var document = JsonDocument.Parse(output);
                return new(process.ExitCode, document.RootElement.Clone(), error);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Groundwork schema tool emitted invalid JSON (exit {process.ExitCode}); stderr digest {Digest(error)}.",
                    exception);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Groundwork schema tool command '{command}' exceeded its {CommandTimeout.TotalSeconds:0}-second timeout.",
                exception);
        }
        finally
        {
            await TerminateAndReapAsync(process);
        }
    }

    public static string ToolPackageVersion()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Join(FindRepositoryRoot(), ".config", "dotnet-tools.json")));
        return manifest.RootElement
                   .GetProperty("tools")
                   .GetProperty("groundwork.tool")
                   .GetProperty("version")
                   .GetString()
               ?? throw new InvalidOperationException("The Groundwork tool manifest omits its package version.");
    }

    private static async Task TerminateAndReapAsync(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The process has no associated operating-system handle left to reap.
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Join(current.FullName, "Elsa.Server.slnx")))
            current = current.Parent;
        return current?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root for Groundwork.Tool.");
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record GroundworkCliResult(int ExitCode, JsonElement Report, string StandardError)
{
    public Exception ToException(string message) =>
        new InvalidOperationException($"{message} (exit {ExitCode}; stderr digest {Digest(StandardError)}).");

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
