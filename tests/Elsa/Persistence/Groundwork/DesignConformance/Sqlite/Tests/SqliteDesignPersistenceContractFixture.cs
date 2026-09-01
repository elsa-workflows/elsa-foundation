using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
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
using Groundwork.Kernel.Schema;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
// These fixtures sit between the design lane and the v1 querying lane, which still declare
// same-named atomic-write types. The design lane is the one under test here.
using IDesignAtomicWriter = Elsa.Workflows.Design.Persistence.Groundwork.IDesignAtomicWriter;
using GroundworkDesignAtomicWriteRequest = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteRequest;
using GroundworkDesignOperationIdentity = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignOperationIdentity;
using GroundworkDocumentWriter = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDocumentWriter;
using GroundworkDesignAtomicWriteResult = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteResult;
using GroundworkDesignAtomicWriteContext = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteContext;
using GroundworkDesignAtomicWriteStageResult = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteStageResult;
using GroundworkDesignSaveRequest = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignSaveRequest;
using GroundworkDesignAtomicCommand = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicCommand;
using GroundworkDesignAtomicWriteStatus = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteStatus;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

internal sealed class SqliteDesignPersistenceContractFixture : IDesignPersistenceContractFixture
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"elsa-design-groundwork-{Guid.NewGuid():N}");
    private readonly GroundworkBaselineTelemetry _telemetry;
    private readonly GroundworkTargetEventCapture _events;
    private readonly GroundworkTargetAtomicityFaultController _atomicityFaults = new();
    // This is a fixture-local continuation observation for the raw-document atomicity probe. It
    // intentionally does not stand in for an IEvent or a workflow lifecycle publication.
    private readonly ConcurrentDictionary<string, byte> _postCommitAtomicOutcomes = new(StringComparer.Ordinal);
    private ServiceProvider _services = null!;
    private CancellationTokenSource? _backgroundEventCancellation;
    private IReadOnlyList<IBackgroundTask> _backgroundEventTasks = [];
    private Task[] _backgroundEventExecutions = [];

    private SqliteDesignPersistenceContractFixture(GroundworkBaselineTelemetry telemetry)
    {
        _telemetry = telemetry;
        _events = new(telemetry);
    }

    public string Provider => "groundwork-sqlite";
    public int RestartCount { get; private set; }
    public int BoundScopeCount { get; private set; }

    private string DatabasePath => Path.Join(_directory, "design.db");
    private string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>The applied target database's connection string, for leaf-local plan capture.</summary>
    internal string SqliteConnectionString => ConnectionString;

    public static async Task<SqliteDesignPersistenceContractFixture> CreateAsync(
        GroundworkBaselineTelemetry telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        var fixture = new SqliteDesignPersistenceContractFixture(telemetry);
        Directory.CreateDirectory(fixture._directory);
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

    /// <summary>
    /// Re-runs admission. Under v2 that is what readiness means: the storage session source applies every
    /// declared unit and throws if the target cannot carry them, so a second pass over a ready database is
    /// an idempotent no-op and a broken one fails here.
    /// </summary>
    public Task ValidateReadinessAsync(CancellationToken cancellationToken = default) =>
        _services.InitializeGroundworkStoreAsync(cancellationToken);

    internal IReadOnlyList<GroundworkRuntimeSchemaAdmissionStatus> InspectRuntimeAdmission()
    {
        var connection = _services.GetRequiredService<IStorageProviderConnection>();
        return _services.GetRequiredService<GroundworkStorageUnitRegistry>().Registrations
            .Select(registration => connection.Schema.InspectRuntimeAdmission(
                registration.Unit,
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = false }).Status)
            .ToArray();
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
        var storage = scope.ServiceProvider.GetRequiredService<GroundworkDesignStorage>();
        var identities = AtomicityDocumentIdentities.Create(storageScope, AtomicitySnapshotOperationKey);
        var definition = storage.Read(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            identities.DefinitionId);
        var version = storage.Read(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            identities.VersionId);
        var marker = storage.Read(
            WorkflowsDesignStorageManifest.DesignOperationDocumentKind,
            MarkerId(AtomicitySnapshotOperationKey));
        var visibleParts = new[] { definition, version }.Count(x => x is not null);
        var markerResultFingerprint = marker is null ? null : MarkerResultFingerprint(Content(marker));

        return new DesignAtomicitySnapshot(
            VisibleAggregatePartCount: visibleParts,
            ExpectedAggregatePartCount: 2,
            DurableOutcomeCount: marker is null ? 0 : 1,
            PostCommitOutcomeCount: marker is not null && _postCommitAtomicOutcomes.ContainsKey(
                OutcomeKey(storageScope, AtomicitySnapshotOperationKey)) ? 1 : 0,
            CanonicalAggregateStateFingerprint: definition is not null && version is not null
                ? Digest($"{Content(definition)}\n{Content(version)}")
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
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named test directory is harmless if SQLite releases a sidecar late.
        }
    }

    private async Task OpenAndAdmitAsync(CancellationToken cancellationToken)
    {
        _services = BuildServices();
        await _services.InitializeGroundworkStoreAsync(cancellationToken);

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
        services.AddGroundworkSqliteUnifiedPersistence(ConnectionString);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        services.AddScoped<IEventHandler<DraftValidating>>(
            _ => new GroundworkTargetCaptureHandler<DraftValidating>(_events));
        services.AddScoped<IEventHandler<DraftCreated>, GroundworkTargetDraftCreatedCaptureHandler>();
        services.AddScoped<IEventHandler<ActivityVersionsReconciling>>(sp =>
            new GroundworkTargetReconciliationHandler(
                sp.GetRequiredService<IPersistenceAccessContextAccessor>(),
                _events));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static GroundworkDesignSaveRequest DefinitionSave(AtomicityDocumentIdentities identities, string storageScope)
    {
        var definition = new WorkflowDefinition
        {
            Id = identities.DefinitionId,
            TenantId = storageScope,
            Name = $"Atomicity probe {identities.Fingerprint}",
            Description = "SQLite design-conformance atomicity probe",
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

    private static GroundworkDesignSaveRequest VersionSave(AtomicityDocumentIdentities identities, string storageScope)
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

    /// <summary>Mirrors the design lane's own marker identity so the snapshot reads the row it writes.</summary>
    private static string MarkerId(string operationKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat("elsa-design-operation:v1", '\u001f', AtomicityOperationKind, '\u001f', operationKey))));

    private static string? MarkerResultFingerprint(string contentJson)
    {
        using var marker = JsonDocument.Parse(contentJson);
        return marker.RootElement.TryGetProperty("resultFingerprint", out var value)
            ? value.GetString()
            : null;
    }

    /// <summary>Reads a row's JSON payload, which a provider may hand back as text or as an element.</summary>
    private static string Content(GroundworkDesignEntry entry) =>
        entry.Entry.Values.Values[WorkflowsDesignStorageManifest.ContentField] switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            var other => throw new InvalidOperationException(
                $"Design-operation marker content was '{other?.GetType().Name ?? "null"}', not JSON.")
        };

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
            throw new InvalidOperationException("Only one SQLite atomicity fault may be armed at a time.");

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
                throw new InvalidOperationException($"Injected SQLite atomicity fault at '{phase}'.");
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
                throw new InvalidOperationException("Injected SQLite atomicity provider-decision fault.");
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
        // The SQLite lifecycle evidence exercises ScopeA, so the background handler binds that
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


internal sealed class GroundworkBaselineTelemetry
{
    private readonly object _sync = new();
    private readonly List<string> _eventTypes = [];
    private int _restartCount;
    private int _boundScopeCount;
    private int _candidateCount;
    private int _reconciliationPassCount;


    public void RecordRestart() => Interlocked.Increment(ref _restartCount);
    public void RecordBoundScope() => Interlocked.Increment(ref _boundScopeCount);
    public void RecordCandidates(int count) => Interlocked.Add(ref _candidateCount, count);
    public void RecordReconciliationPass() => Interlocked.Increment(ref _reconciliationPassCount);

    public void RecordEvent(Type eventType)
    {
        lock (_sync)
            _eventTypes.Add(eventType.FullName ?? eventType.Name);
    }

    public GroundworkBaselineTelemetrySnapshot Snapshot()
    {
        lock (_sync)
        {
            var canonicalEventTypes = string.Join(
                "\n",
                _eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return new(
                _restartCount,
                _boundScopeCount,
                _candidateCount,
                _reconciliationPassCount,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalEventTypes))).ToLowerInvariant());
        }
    }
}

internal sealed record GroundworkBaselineTelemetrySnapshot(
    int RestartCount,
    int BoundScopeCount,
    int ReconciliationCandidateCount,
    int ReconciliationPassCount,
    string EventTypeDigest);
