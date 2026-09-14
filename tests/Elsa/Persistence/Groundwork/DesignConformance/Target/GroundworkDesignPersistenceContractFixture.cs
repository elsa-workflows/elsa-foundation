using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
// The design lane and the v1 querying lane still declare same-named atomic-write types. The design lane is
// the one under test here.
using IDesignAtomicWriter = Elsa.Workflows.Design.Persistence.Groundwork.IDesignAtomicWriter;
using GroundworkDesignAtomicWriteRequest = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteRequest;
using GroundworkDesignOperationIdentity = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignOperationIdentity;
using GroundworkDocumentWriter = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDocumentWriter;
using GroundworkDesignAtomicWriteResult = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteResult;
using GroundworkDesignAtomicWriteStageResult = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteStageResult;
using GroundworkDesignSaveRequest = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignSaveRequest;
using GroundworkDesignAtomicWriteStatus = Elsa.Workflows.Design.Persistence.Groundwork.GroundworkDesignAtomicWriteStatus;

namespace Elsa.Persistence.Groundwork.DesignConformance.Target;

/// <summary>
/// The composed Groundwork design-persistence target that every provider leaf runs the shared contract
/// suites against. A provider derives from it and supplies only its connection, its display name, and any
/// provider-specific cleanup; everything the suites observe (scoped access, restart, readiness, event
/// capture, reconciliation staging, and the raw-document atomicity probe) is composed here once.
/// </summary>
public abstract class GroundworkDesignPersistenceContractFixture : IDesignPersistenceContractFixture
{
    private const string AtomicityOperationKind = "design-conformance.atomicity.v1";
    private const string AtomicitySnapshotOperationKey = "design-atomicity-create-v1";
    private const char UnitSeparator = (char)0x1F;

    private readonly string _providerDisplayName;
    private readonly GroundworkTargetEventCapture _events = new();
    private readonly GroundworkTargetAtomicityFaultController _atomicityFaults;
    // A fixture-local continuation observation for the raw-document atomicity probe. It intentionally does
    // not stand in for an IEvent or a workflow lifecycle publication.
    private readonly ConcurrentDictionary<string, byte> _postCommitAtomicOutcomes = new(StringComparer.Ordinal);
    private ServiceProvider _services = null!;
    private CancellationTokenSource? _backgroundEventCancellation;
    private IReadOnlyList<IBackgroundTask> _backgroundEventTasks = [];
    private Task[] _backgroundEventExecutions = [];

    protected GroundworkDesignPersistenceContractFixture(string provider, string providerDisplayName)
    {
        Provider = provider;
        _providerDisplayName = providerDisplayName;
        _atomicityFaults = new(providerDisplayName);
    }

    public string Provider { get; }
    public int RestartCount { get; private set; }
    public int BoundScopeCount { get; private set; }

    /// <summary>The composed root services, for provider-level admission probes.</summary>
    protected IServiceProvider Services => _services;

    /// <summary>Creates the one provider connection for this fixture's isolated target database.</summary>
    protected abstract IStorageProviderConnection CreateConnection();

    /// <summary>Provider-specific cleanup of the isolated target, after the composed services are disposed.</summary>
    protected virtual void CleanUp()
    {
    }

    /// <summary>Composes, admits, and starts the background event pump of a freshly constructed fixture.</summary>
    protected static async Task<TFixture> OpenAsync<TFixture>(TFixture fixture, CancellationToken cancellationToken)
        where TFixture : GroundworkDesignPersistenceContractFixture
    {
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
        await OpenAndAdmitAsync(cancellationToken);
    }

    /// <summary>
    /// Re-runs admission. Under v2 that is what readiness means: the storage session source applies every
    /// declared unit and throws if the target cannot carry them, so a second pass over a ready database is
    /// an idempotent no-op and a broken one fails here.
    /// </summary>
    public Task ValidateReadinessAsync(CancellationToken cancellationToken = default) =>
        _services.InitializeGroundworkStoreAsync(cancellationToken);

    public Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Stage(storageScope, candidates);
        return Task.CompletedTask;
    }

    public void ClearObservedEvents() => _events.Clear();

    public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_events.Snapshot());
    }

    public Task<DraftCreated> WaitForPublishedDraftCreatedAsync(string draftId, CancellationToken cancellationToken = default)
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
            new GroundworkDesignOperationIdentity(AtomicityOperationKind, request.OperationKey.Value),
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
                _atomicityFaults.ThrowIfTriggered(DesignAtomicityFaultPhase.AfterStagedWrite, operationCancellation);
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

        _atomicityFaults.ThrowIfTriggered(DesignAtomicityFaultPhase.AfterDurableDecision, operationCancellation);
        return mapped;
    }

    public Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
        string storageScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageScope);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = CreateScope(storageScope);
        var storage = scope.ServiceProvider.GetRequiredService<GroundworkDesignStorage>();
        var identities = AtomicityDocumentIdentities.Create(storageScope, AtomicitySnapshotOperationKey);
        var definition = storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, identities.DefinitionId);
        var version = storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, identities.VersionId);
        var marker = storage.Read(WorkflowsDesignStorageManifest.DesignOperationDocumentKind, MarkerId(AtomicitySnapshotOperationKey));

        return Task.FromResult(new DesignAtomicitySnapshot(
            VisibleAggregatePartCount: new[] { definition, version }.Count(x => x is not null),
            ExpectedAggregatePartCount: 2,
            DurableOutcomeCount: marker is null ? 0 : 1,
            PostCommitOutcomeCount: marker is not null && _postCommitAtomicOutcomes.ContainsKey(
                OutcomeKey(storageScope, AtomicitySnapshotOperationKey)) ? 1 : 0,
            CanonicalAggregateStateFingerprint: definition is not null && version is not null
                ? Digest($"{Content(definition)}\n{Content(version)}")
                : null,
            AuthoritativeDurableResultFingerprint: marker is null ? null : MarkerResultFingerprint(Content(marker))));
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await StopBackgroundEventsAsync(CancellationToken.None);
            await _services.DisposeAsync();
        }

        CleanUp();
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
            new GroundworkTargetDeferredEventPublisher(sp.GetRequiredService<IEventPublisher>(), _events));
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddDesignPersistencePublicationDeletionGuard();
        services.AddGroundworkStorageProviderConnection(_ => CreateConnection());
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        services.AddScoped<IEventHandler<DraftValidating>>(_ => new GroundworkTargetCaptureHandler<DraftValidating>(_events));
        services.AddScoped<IEventHandler<DraftCreated>, GroundworkTargetDraftCreatedCaptureHandler>();
        services.AddScoped<IEventHandler<ActivityVersionsReconciling>>(sp =>
            new GroundworkTargetReconciliationHandler(sp.GetRequiredService<IPersistenceAccessContextAccessor>(), _events));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private GroundworkDesignSaveRequest DefinitionSave(AtomicityDocumentIdentities identities, string storageScope)
    {
        var definition = new WorkflowDefinition
        {
            Id = identities.DefinitionId,
            TenantId = storageScope,
            Name = $"Atomicity probe {identities.Fingerprint}",
            Description = $"{_providerDisplayName} design-conformance atomicity probe",
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
            GroundworkDesignAtomicWriteStatus.Committed or GroundworkDesignAtomicWriteStatus.Reconciled =>
                new(DesignAtomicityOperationStatus.Committed, result.AuthoritativeResultFingerprint),
            GroundworkDesignAtomicWriteStatus.Replayed =>
                new(DesignAtomicityOperationStatus.Replayed, result.AuthoritativeResultFingerprint),
            GroundworkDesignAtomicWriteStatus.Rejected => new(DesignAtomicityOperationStatus.Rejected, null),
            GroundworkDesignAtomicWriteStatus.Conflict => new(DesignAtomicityOperationStatus.Conflict, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    private static string OutcomeKey(string storageScope, string operationKey) => $"{storageScope}\n{operationKey}";

    private static string ResultFingerprint(string storageScope, string requestFingerprint, AtomicityDocumentIdentities identities) =>
        Digest($"{storageScope}\n{requestFingerprint}\n{identities.DefinitionId}\n{identities.VersionId}");

    private static string ResultJson(string storageScope, string requestFingerprint, AtomicityDocumentIdentities identities) =>
        JsonSerializer.Serialize(new { storageScope, requestFingerprint, identities.DefinitionId, identities.VersionId });

    /// <summary>Mirrors the design lane's own marker identity so the snapshot reads the row it writes.</summary>
    private static string MarkerId(string operationKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat("elsa-design-operation:v1", UnitSeparator, AtomicityOperationKind, UnitSeparator, operationKey))));

    private static string? MarkerResultFingerprint(string contentJson)
    {
        using var marker = JsonDocument.Parse(contentJson);
        return marker.RootElement.TryGetProperty("resultFingerprint", out var value) ? value.GetString() : null;
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
