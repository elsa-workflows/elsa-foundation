using Elsa.ConsumerGuide.Testing;
using System.Collections.Concurrent;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public class GroundworkWorkflowDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    // Shared arrange graph: one document store, one lock provider, one hook publisher, and one
    // identity generator per test instance. Every command/store below composes over these, so tests
    // read as intent (seed → act → derive) instead of repeating the wiring (CLAUDE.md clean-tests).
    private readonly InMemoryDocumentStore _store = new(WorkflowsDesignStorageManifest.Create());
    private readonly InMemoryLockProvider _locks = new();
    private readonly HookEventPublisher _events = new();
    private readonly SequentialIdentityGenerator _identities = new();
    private readonly FakeSystemClock _clock = new();
    private readonly Elsa.Persistence.Core.IPersistenceAccessContextAccessor _accessContext =
        GroundworkTestAccess.DefaultAccessContextAccessor;
    private int _operationSequence;

    private GroundworkDesignAtomicWrite AtomicWrite() => new(_store);

    private DesignOperationKey NextKey() => new($"test-operation-{++_operationSequence}");

    private IDraftOriginator DraftOriginator(
        IDocumentStore? store = null,
        IPayloadSerializer? payloadSerializer = null,
        IDeferredEventPublisher? deferredEventPublisher = null)
    {
        var documents = store ?? _store;
        var payloads = payloadSerializer ?? Payloads;
        return new DraftOriginator(
            _identities,
            _locks,
            documents,
            new GroundworkDesignAtomicWrite(documents),
            payloads,
            _events,
            deferredEventPublisher ?? _events,
            _clock,
            _accessContext);
    }

    private GroundworkCreateDraftCommand CreateCommand() =>
        new(DraftOriginator(), Payloads);

    private GroundworkUpdateDraftCommand UpdateCommand(IActivityStructureService? activityStructureService = null) =>
        new(
            _locks,
            _store,
            AtomicWrite(),
            Payloads,
            _events,
            _events,
            _clock,
            _accessContext,
            activityStructureService ?? new EmptyActivityStructureService());

    private GroundworkWorkflowDefinitionVersionStore VersionStore(IDocumentStore? store = null)
    {
        var documents = store ?? _store;
        return new(documents, new GroundworkWorkflowDefinitionStore(documents), Payloads);
    }

    private GroundworkPromoteDraftToVersionCommand PromoteCommand(IDocumentStore? store = null)
    {
        var documents = store ?? _store;
        return new(
            _locks,
            documents,
            new GroundworkDesignAtomicWrite(documents),
            Payloads,
            _events,
            VersionStore(documents),
            _identities,
            _clock,
            _accessContext);
    }

    private GroundworkMaterializeWorkflowDefinitionVersionCommand MaterializeVersionCommand() =>
        new(AtomicWrite(), Payloads, _clock, _accessContext);

    private Task MaterializeDefinition(WorkflowDefinition definition) =>
        new GroundworkMaterializeWorkflowDefinitionCommand(AtomicWrite(), _clock, _accessContext)
            .Execute(NextKey(), definition, CancellationToken.None);

    // Draft store reads only the draft/layout document; it never derives validation errors, so no
    // publisher is threaded through it (the gate is invoked directly on the publisher below).
    private GroundworkWorkflowDefinitionDraftStore DraftStore() => new(_store, Payloads, _accessContext);

    private GroundworkWorkflowDefinitionVersionLayoutStore VersionLayoutStore() => new(_store);

    /// <summary>
    /// Derives the current validation error set the same way production does: load the draft, then
    /// run the gate through the hook publisher (which re-publishes <c>OnDraftValidating</c>). Replaces
    /// the retired store derive-port.
    /// </summary>
    private async Task<IReadOnlyList<ValidationError>> DeriveErrors(string draftId)
    {
        var draft = await DraftStore().FindByIdAsync(draftId);
        Assert.NotNull(draft);
        return await _events.DeriveValidationErrorsAsync(draft!, CancellationToken.None);
    }

    [Fact]
    public async Task CreateDraft_persists_layout_and_derives_validation_errors_from_the_draft_document()
    {
        _events.ContributeError(new ValidationError("$workflow", "Test/Error", "Invalid"));
        var layout = new[] { new DesignMetadataRecord("root", 10, 20, 300, 200) };

        var draftId = await CreateCommand().Execute(NextKey(), "definition-1", EmptyState(), layout, cancellationToken: CancellationToken.None);

        // Errors are derived state (no persisted sibling): deriving through the gate re-runs the hook,
        // which contributes the error again on demand.
        var draftStore = DraftStore();
        var draft = await draftStore.FindByIdAsync(draftId);
        var readLayout = await draftStore.FindLayoutByDraftIdAsync(draftId);
        var errors = await DeriveErrors(draftId);

        Assert.NotNull(draft);
        Assert.Equal("definition-1", draft!.WorkflowDefinitionId);
        Assert.Equal(layout.Single(), readLayout.Single());
        Assert.Equal("Test/Error", errors.Single().Type);
        Assert.Equal(1, _locks.AcquireCounts[WorkflowDesignPersistenceLockKeys.DraftKey(draftId)]);
    }

    [Fact]
    public async Task CreateDraft_maps_payload_serialization_failure_before_opening_an_atomic_write()
    {
        var serializationFailure = new InvalidOperationException("Payload serialization failed.");
        var failingPayloads = new FakePayloadSerializer(serializationFailure);
        var command = new GroundworkCreateDraftCommand(
            DraftOriginator(payloadSerializer: failingPayloads),
            failingPayloads);

        var thrown = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(
            NextKey(),
            "definition-serialization-failure",
            EmptyState(),
            cancellationToken: CancellationToken.None));

        Assert.Equal(DesignPersistenceDomain.Workflow, thrown.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Serialization, thrown.FailureKind);
        Assert.Equal("workflow.draft.create.v1", thrown.Operation);
        Assert.Same(serializationFailure, thrown.InnerException);
        Assert.Equal(0, _store.BeginCount);
        Assert.Empty(_store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
    }

    [Fact]
    public async Task CloneDraftFromVersion_copies_the_source_state_and_layout_and_replays_the_created_draft()
    {
        var state = new WorkflowDefinitionState(
            [],
            new ActivityNode("root", "activity-version-1", [], []),
            [],
            [],
            null);
        var layout = new[] { new DesignMetadataRecord("root", 10, 20, 300, 200) };
        var activityPresentation = new[]
        {
            new ActivityPresentationRecord(
                "root",
                "Notify buyer",
                "Send the confirmation after payment.")
        };
        var sourceDraftId = await CreateCommand().Execute(NextKey(), "definition-1", state, layout, cancellationToken: CancellationToken.None);
        await UpdateCommand().Execute(
            NextKey(),
            new UpdateDraftRequest(sourceDraftId, state, layout, activityPresentation),
            CancellationToken.None);
        var sourceVersionId = await PromoteCommand().Execute(NextKey(), sourceDraftId, CancellationToken.None);
        var clone = new GroundworkCloneDraftFromVersionCommand(
            VersionStore(),
            VersionLayoutStore(),
            DraftOriginator(),
            _accessContext);
        var key = NextKey();

        var cloneId = await clone.Execute(key, sourceVersionId, CancellationToken.None);
        var savesAfterClone = _store.SaveCount;
        var replayId = await clone.Execute(key, sourceVersionId, CancellationToken.None);

        var cloned = await DraftStore().FindByIdAsync(cloneId);
        Assert.Equal(cloneId, replayId);
        Assert.Equal(savesAfterClone, _store.SaveCount);
        Assert.NotNull(cloned);
        Assert.Equal(sourceVersionId, cloned!.SourceVersionId);
        Assert.NotNull(cloned.State.RootActivity);
        Assert.Equal(state.RootActivity!.NodeId, cloned.State.RootActivity!.NodeId);
        Assert.Equal(state.RootActivity.ActivityVersionId, cloned.State.RootActivity.ActivityVersionId);
        Assert.Empty(cloned.State.RootActivity.Inputs);
        Assert.Empty(cloned.State.RootActivity.Outputs);
        Assert.Empty(cloned.State.Variables);
        Assert.Empty(cloned.State.Inputs);
        Assert.Empty(cloned.State.Outputs);
        Assert.Equal(layout, await DraftStore().FindLayoutByDraftIdAsync(cloneId));
        Assert.Equal(
            activityPresentation,
            await DraftStore().FindActivityPresentationByDraftIdAsync(cloneId));
    }

    [Fact]
    public async Task CloneDraftFromVersion_replay_returns_the_committed_draft_after_the_source_is_removed_without_restaging_or_republishing()
    {
        var sourceDraftId = await CreateCommand().Execute(
            NextKey(),
            "definition-1",
            MinimalState(),
            [new DesignMetadataRecord("root", 10, 20, 300, 200)],
            cancellationToken: CancellationToken.None);
        var sourceVersionId = await PromoteCommand().Execute(NextKey(), sourceDraftId, CancellationToken.None);
        var sourceLayout = await VersionLayoutStore().FindByVersionIdAsync(sourceVersionId);
        Assert.NotNull(sourceLayout);
        var clone = new GroundworkCloneDraftFromVersionCommand(
            VersionStore(),
            VersionLayoutStore(),
            DraftOriginator(),
            _accessContext);
        var operationKey = NextKey();
        var deferredEventsBeforeClone = _events.DeferredEvents.Count;
        var cloneId = await clone.Execute(operationKey, sourceVersionId, CancellationToken.None);
        var identityCount = _identities.GenerateCount;
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(cloneId);
        var draftLockCount = _locks.AcquireCounts[draftLockKey];
        var deferredEventCount = _events.DeferredEvents.Count;
        Assert.Equal(deferredEventsBeforeClone + 2, deferredEventCount);

        await _store.DeleteAsync(GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            sourceLayout!.Id));
        await _store.DeleteAsync(GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            sourceVersionId));
        var beginCount = _store.BeginCount;
        var saveCount = _store.SaveCount;
        var deleteCount = _store.DeleteCount;

        var replayId = await clone.Execute(operationKey, sourceVersionId, CancellationToken.None);

        Assert.Equal(cloneId, replayId);
        Assert.Equal(beginCount, _store.BeginCount);
        Assert.Equal(saveCount, _store.SaveCount);
        Assert.Equal(deleteCount, _store.DeleteCount);
        Assert.Equal(identityCount, _identities.GenerateCount);
        Assert.Equal(draftLockCount, _locks.AcquireCounts[draftLockKey]);
        Assert.Equal(deferredEventCount, _events.DeferredEvents.Count);
        Assert.DoesNotContain(
            _store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind),
            document => document.Id == sourceVersionId);
        Assert.NotNull(await DraftStore().FindByIdAsync(cloneId));
    }

    [Fact]
    public async Task CloneDraftFromVersion_same_key_with_different_source_conflicts_before_reading_the_alternative_source()
    {
        var sourceDraftId = await CreateCommand().Execute(
            NextKey(),
            "definition-1",
            MinimalState(),
            cancellationToken: CancellationToken.None);
        var sourceVersionId = await PromoteCommand().Execute(NextKey(), sourceDraftId, CancellationToken.None);
        var clone = new GroundworkCloneDraftFromVersionCommand(
            VersionStore(),
            VersionLayoutStore(),
            DraftOriginator(),
            _accessContext);
        var operationKey = NextKey();
        await clone.Execute(operationKey, sourceVersionId, CancellationToken.None);
        var beginCount = _store.BeginCount;
        var saveCount = _store.SaveCount;
        var identityCount = _identities.GenerateCount;
        var lockCount = _locks.AcquireCounts.Values.Sum();
        var deferredEventCount = _events.DeferredEvents.Count;

        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() =>
            clone.Execute(operationKey, "missing-alternative-version", CancellationToken.None));

        Assert.Equal(beginCount, _store.BeginCount);
        Assert.Equal(saveCount, _store.SaveCount);
        Assert.Equal(identityCount, _identities.GenerateCount);
        Assert.Equal(lockCount, _locks.AcquireCounts.Values.Sum());
        Assert.Equal(deferredEventCount, _events.DeferredEvents.Count);
    }

    [Fact]
    public async Task CloneDraftFromVersion_reconciles_lost_acknowledgement_then_replays_without_the_source_or_duplicate_events()
    {
        var sourceDraftId = await CreateCommand().Execute(
            NextKey(),
            "definition-1",
            MinimalState(),
            [new DesignMetadataRecord("root", 10, 20)],
            cancellationToken: CancellationToken.None);
        var sourceVersionId = await PromoteCommand().Execute(NextKey(), sourceDraftId, CancellationToken.None);
        var sourceLayout = await VersionLayoutStore().FindByVersionIdAsync(sourceVersionId);
        Assert.NotNull(sourceLayout);
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(_store, callerCancellation);
        var deferredEvents = new CancellationAwareDeferredEventPublisher();
        var clone = new GroundworkCloneDraftFromVersionCommand(
            VersionStore(),
            VersionLayoutStore(),
            DraftOriginator(store, deferredEventPublisher: deferredEvents),
            _accessContext);
        var operationKey = NextKey();

        var cloneId = await clone.Execute(operationKey, sourceVersionId, callerCancellation.Token);
        await _store.DeleteAsync(GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            sourceLayout!.Id));
        await _store.DeleteAsync(GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            sourceVersionId));
        var identityCount = _identities.GenerateCount;
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(cloneId);
        var draftLockCount = _locks.AcquireCounts[draftLockKey];
        var replayId = await clone.Execute(operationKey, sourceVersionId, CancellationToken.None);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Equal(cloneId, replayId);
        Assert.Equal(identityCount, _identities.GenerateCount);
        Assert.Equal(draftLockCount, _locks.AcquireCounts[draftLockKey]);
        Assert.Collection(
            deferredEvents.Events,
            @event => Assert.IsType<OnDraftCreated>(@event),
            @event => Assert.IsType<OnDraftValidated>(@event));
        Assert.All(deferredEvents.CancellationTokens, token => Assert.False(token.IsCancellationRequested));
        Assert.NotNull(await DraftStore().FindByIdAsync(cloneId));
    }

    [Fact]
    public async Task Materialize_definition_and_version_replay_without_restaging_and_conflict_on_changed_material()
    {
        var definitionCommand = new GroundworkMaterializeWorkflowDefinitionCommand(AtomicWrite(), _clock, _accessContext);
        var definitionKey = NextKey();
        var definition = new WorkflowDefinition { Id = "definition-1", Name = "Original", IsSourceOwned = true };

        var firstDefinitionId = await definitionCommand.Execute(definitionKey, definition, CancellationToken.None);
        var savesAfterDefinition = _store.SaveCount;
        var replayDefinitionId = await definitionCommand.Execute(
            definitionKey,
            new WorkflowDefinition { Id = "definition-1", Name = "Original", IsSourceOwned = true },
            CancellationToken.None);

        Assert.Equal(firstDefinitionId, replayDefinitionId);
        Assert.Equal(savesAfterDefinition, _store.SaveCount);
        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() => definitionCommand.Execute(
            definitionKey,
            new WorkflowDefinition { Id = "definition-1", Name = "Changed", IsSourceOwned = true },
            CancellationToken.None));

        var versionCommand = MaterializeVersionCommand();
        var versionKey = NextKey();
        var version = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1",
            State = EmptyState()
        };

        var firstVersion = await versionCommand.Execute(versionKey, version, CancellationToken.None);
        var savesAfterVersion = _store.SaveCount;
        var replayVersion = await versionCommand.Execute(
            versionKey,
            new WorkflowDefinitionVersion("definition-1", "1.0.0") { Id = "version-1", State = EmptyState() },
            CancellationToken.None);

        Assert.Equal(firstVersion, replayVersion);
        Assert.Equal(savesAfterVersion, _store.SaveCount);
        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() => versionCommand.Execute(
            versionKey,
            new WorkflowDefinitionVersion("definition-1", "2.0.0") { Id = "version-2", State = EmptyState() },
            CancellationToken.None));
    }

    [Fact]
    public async Task Materialize_definition_rolls_back_provider_failures_and_propagates_cancellation()
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var providerFailure = new IOException("provider unavailable");
        var failing = new GroundworkMaterializeWorkflowDefinitionCommand(
            new GroundworkDesignAtomicWrite(new ThrowingDocumentStore(raw, GroundworkDocumentStoreOperation.Begin, providerFailure)),
            _clock,
            _accessContext);

        var thrown = await Assert.ThrowsAsync<DesignPersistenceException>(() => failing.Execute(
            NextKey(),
            new WorkflowDefinition { Id = "definition-provider-failure", Name = "Unavailable" },
            CancellationToken.None));

        Assert.Equal(DesignPersistenceDomain.Workflow, thrown.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Provider, thrown.FailureKind);
        Assert.Same(providerFailure, thrown.InnerException);
        Assert.Empty(raw.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GroundworkMaterializeWorkflowDefinitionCommand(
            new GroundworkDesignAtomicWrite(_store),
            _clock,
            _accessContext).Execute(
            NextKey(),
            new WorkflowDefinition { Id = "definition-cancelled", Name = "Cancelled" },
            cancellation.Token));
        Assert.Null(await new GroundworkWorkflowDefinitionStore(_store).FindByIdAsync("definition-cancelled"));
    }

    [Fact]
    public async Task Create_update_and_discard_publish_deferred_events_only_after_commit_and_never_on_replay()
    {
        _events.OnPublish = @event =>
        {
            if (@event is OnDraftCreated or OnDraftValidated)
                Assert.NotEmpty(_store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
            if (@event is OnDraftDiscarded)
                Assert.Empty(_store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
        };
        var createKey = NextKey();
        var draftId = await CreateCommand().Execute(createKey, "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        await CreateCommand().Execute(createKey, "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var updateKey = NextKey();
        await UpdateCommand().Execute(updateKey, new UpdateDraftRequest(draftId, EmptyState(), []), CancellationToken.None);
        await UpdateCommand().Execute(updateKey, new UpdateDraftRequest(draftId, EmptyState(), []), CancellationToken.None);
        var discard = new GroundworkDiscardDraftCommand(_locks, _store, AtomicWrite(), Payloads, _events, _accessContext);
        var discardKey = NextKey();
        await discard.Execute(discardKey, draftId, CancellationToken.None);
        await discard.Execute(discardKey, draftId, CancellationToken.None);

        Assert.Collection(
            _events.DeferredEvents,
            @event => Assert.IsType<OnDraftCreated>(@event),
            @event => Assert.IsType<OnDraftValidated>(@event),
            @event => Assert.IsType<OnDraftValidated>(@event),
            @event => Assert.IsType<OnDraftDiscarded>(@event));
    }

    [Fact]
    public async Task CreateDraft_reconciles_lost_acknowledgement_and_enqueues_events_despite_caller_cancellation_without_replaying_them()
    {
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(_store, callerCancellation);
        var deferredEvents = new CancellationAwareDeferredEventPublisher();
        var command = new GroundworkCreateDraftCommand(
            DraftOriginator(store, deferredEventPublisher: deferredEvents),
            Payloads);
        var key = NextKey();

        var draftId = await command.Execute(key, "definition-1", EmptyState(), cancellationToken: callerCancellation.Token);
        var replay = await command.Execute(key, "definition-1", EmptyState(), cancellationToken: CancellationToken.None);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Equal(draftId, replay);
        Assert.Collection(
            deferredEvents.Events,
            @event => Assert.IsType<OnDraftCreated>(@event),
            @event => Assert.IsType<OnDraftValidated>(@event));
        Assert.All(deferredEvents.CancellationTokens, token => Assert.False(token.IsCancellationRequested));
    }

    [Fact]
    public async Task UpdateDraft_reconciles_lost_acknowledgement_and_enqueues_event_despite_caller_cancellation_without_replaying_it()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(_store, callerCancellation);
        var deferredEvents = new CancellationAwareDeferredEventPublisher();
        var command = new GroundworkUpdateDraftCommand(
            _locks,
            store,
            new GroundworkDesignAtomicWrite(store),
            Payloads,
            _events,
            deferredEvents,
            _clock,
            _accessContext,
            new EmptyActivityStructureService());
        var key = NextKey();
        var request = new UpdateDraftRequest(draftId, EmptyState(), []);

        await command.Execute(key, request, callerCancellation.Token);
        await command.Execute(key, request, CancellationToken.None);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Collection(deferredEvents.Events, @event => Assert.IsType<OnDraftValidated>(@event));
        Assert.All(deferredEvents.CancellationTokens, token => Assert.False(token.IsCancellationRequested));
    }

    [Fact]
    public async Task DiscardDraft_reconciles_lost_acknowledgement_and_enqueues_event_despite_caller_cancellation_without_replaying_it()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(_store, callerCancellation);
        var deferredEvents = new CancellationAwareDeferredEventPublisher();
        var command = new GroundworkDiscardDraftCommand(
            _locks,
            store,
            new GroundworkDesignAtomicWrite(store),
            Payloads,
            deferredEvents,
            _accessContext);
        var key = NextKey();

        await command.Execute(key, draftId, callerCancellation.Token);
        await command.Execute(key, draftId, CancellationToken.None);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Collection(deferredEvents.Events, @event => Assert.IsType<OnDraftDiscarded>(@event));
        Assert.All(deferredEvents.CancellationTokens, token => Assert.False(token.IsCancellationRequested));
    }

    [Fact]
    public async Task UpdateDraft_replaces_layout_atomically_and_errors_are_derived()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-1", EmptyState(), [new DesignMetadataRecord("old", 0, 0, 100, 100)], cancellationToken: CancellationToken.None);
        _events.ContributeError(new ValidationError("root", "Updated/Error", "Still invalid"));
        var nextLayout = new[] { new DesignMetadataRecord("root", 1, 2, 3, 4) };
        var activityPresentation = new[]
        {
            new ActivityPresentationRecord("root", "Notify buyer", "After payment.")
        };

        await UpdateCommand().Execute(
            NextKey(),
            new UpdateDraftRequest(draftId, MinimalState(), nextLayout, activityPresentation),
            CancellationToken.None);

        var readLayout = await DraftStore().FindLayoutByDraftIdAsync(draftId);
        var readPresentation = await DraftStore().FindActivityPresentationByDraftIdAsync(draftId);
        var errors = await DeriveErrors(draftId);

        Assert.Equal(nextLayout.Single(), readLayout.Single());
        Assert.Equal(activityPresentation, readPresentation);
        Assert.Equal("Updated/Error", errors.Single().Type);
    }

    [Fact]
    public async Task UpdateDraft_prunes_presentation_for_nodes_removed_from_the_submitted_activity_tree()
    {
        var draftId = await CreateCommand().Execute(
            NextKey(),
            "definition-1",
            MinimalState(),
            cancellationToken: CancellationToken.None);
        var child = new ActivityNode("child", "activity-version-1", [], []);
        var structure = new MutableActivityStructureService { InvalidChild = child };
        var command = UpdateCommand(structure);
        var presentation = new[]
        {
            new ActivityPresentationRecord("root", "Root"),
            new ActivityPresentationRecord("child", "Notify buyer")
        };

        await command.Execute(
            NextKey(),
            new UpdateDraftRequest(draftId, MinimalState(), [], presentation),
            CancellationToken.None);

        structure.InvalidChild = null;
        await command.Execute(
            NextKey(),
            new UpdateDraftRequest(draftId, MinimalState(), [], presentation),
            CancellationToken.None);

        var stored = await DraftStore().FindActivityPresentationByDraftIdAsync(draftId);
        Assert.Equal([new ActivityPresentationRecord("root", "Root")], stored);
    }

    [Fact]
    public async Task PromoteDraft_rejects_validation_errors_and_promotes_clean_drafts_with_layout()
    {
        _events.ContributeError(new ValidationError("$workflow", "Blocking/Error", "Nope"));
        var create = CreateCommand();
        var blockedDraftId = await create.Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();

        await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => promote.Execute(NextKey(), blockedDraftId, CancellationToken.None));

        _events.OnPublish = null;
        var layout = new[] { new DesignMetadataRecord("root", 5, 6, 7, 8) };
        var cleanDraftId = await create.Execute(NextKey(), "definition-1", EmptyState(), layout, cancellationToken: CancellationToken.None);

        var versionId = await promote.Execute(NextKey(), cleanDraftId, CancellationToken.None);

        var version = await VersionStore().GetAsync(versionId);
        var versionLayout = await VersionLayoutStore().FindByVersionIdAsync(versionId);

        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(layout.Single(), versionLayout!.Records.Single());
    }

    [Fact]
    public async Task PromoteDraft_rejects_an_unknown_draft_as_not_found()
    {
        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            PromoteCommand().Execute(NextKey(), "draft-missing", CancellationToken.None));

        Assert.Contains("draft-missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromoteDraft_persists_a_requested_forward_release_and_prerelease()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-exact", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();

        var releaseId = await promote.Execute(NextKey(), draftId, "2.0.0", CancellationToken.None);
        var prereleaseId = await promote.Execute(NextKey(), draftId, "2.1.0-rc.1", CancellationToken.None);

        Assert.Equal("2.0.0", (await VersionStore().GetAsync(releaseId)).Version);
        Assert.Equal("2.1.0-rc.1", (await VersionStore().GetAsync(prereleaseId)).Version);
    }

    [Theory]
    [InlineData("not-semver")]
    [InlineData("1.0.0")]
    public async Task PromoteDraft_rejects_malformed_or_non_forward_requested_versions_without_creating_a_version(string requestedVersion)
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-invalid", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();
        await promote.Execute(NextKey(), draftId, "2.0.0", CancellationToken.None);
        var versionCount = (await VersionStore().ListByDefinitionAsync("definition-invalid")).Count;

        await Assert.ThrowsAsync<WorkflowVersionSelectionException>(() =>
            promote.Execute(NextKey(), draftId, requestedVersion, CancellationToken.None));

        Assert.Equal(versionCount, (await VersionStore().ListByDefinitionAsync("definition-invalid")).Count);
    }

    [Fact]
    public async Task PromoteDraft_replay_requires_the_same_requested_version_material()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-replay", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();
        var key = NextKey();

        var versionId = await promote.Execute(key, draftId, "2.0.0", CancellationToken.None);
        var replayId = await promote.Execute(key, draftId, " 2.0.0 ", CancellationToken.None);

        Assert.Equal(versionId, replayId);
        await Assert.ThrowsAsync<WorkflowPromotionOperationConflictException>(() =>
            promote.Execute(key, draftId, "2.1.0", CancellationToken.None));
    }

    [Fact]
    public async Task PromoteDraft_rejects_a_build_metadata_equivalent_identity_as_a_conflict_without_creating_a_version()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-build-conflict", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();
        await promote.Execute(NextKey(), draftId, "2.0.0", CancellationToken.None);
        var versionCount = (await VersionStore().ListByDefinitionAsync("definition-build-conflict")).Count;

        await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() =>
            promote.Execute(NextKey(), draftId, "2.0.0+build.7", CancellationToken.None));

        Assert.Equal(versionCount, (await VersionStore().ListByDefinitionAsync("definition-build-conflict")).Count);
    }

    [Fact]
    public async Task PromoteDraft_maps_a_final_version_identity_save_race_to_a_domain_conflict()
    {
        var draftId = await CreateCommand().Execute(
            NextKey(),
            "definition-save-race",
            EmptyState(),
            cancellationToken: CancellationToken.None);
        var conflictingStore = new VersionSaveConflictDocumentStore(_store);

        await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() =>
            PromoteCommand(conflictingStore).Execute(
                NextKey(),
                draftId,
                "2.0.0",
                CancellationToken.None));

        Assert.True(conflictingStore.VersionSaveWasRejected);
        Assert.Empty(await VersionStore().ListByDefinitionAsync("definition-save-race"));
    }

    [Fact]
    public async Task PromoteDraft_replay_rejects_switching_between_exact_and_automatic_assignment()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-replay-mode", EmptyState(), cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();
        var key = NextKey();
        await promote.Execute(key, draftId, "2.0.0", CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowPromotionOperationConflictException>(() =>
            promote.Execute(key, draftId, requestedVersion: null, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task PromoteDraft_preserves_the_scope_identity_on_the_version_and_layout()
    {
        const string scope = GroundworkTestAccess.DefaultScopeValue;
        var definition = new WorkflowDefinition
        {
            Id = "definition-scoped",
            TenantId = scope,
            Name = "Scoped definition"
        };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-scoped",
            TenantId = scope,
            WorkflowDefinitionId = definition.Id,
            State = EmptyState()
        };
        await new GroundworkAddWorkflowDefinitionCommand(_store, AtomicWrite(), Payloads, _clock, _accessContext).Execute(
            NextKey(),
            definition,
            draft,
            [new DesignMetadataRecord("root", 5, 6, 7, 8)],
            CancellationToken.None);

        var versionId = await PromoteCommand().Execute(NextKey(), draft.Id, CancellationToken.None);

        var version = await VersionStore().GetAsync(versionId);
        var layout = await VersionLayoutStore().FindByVersionIdAsync(versionId);
        Assert.Equal(scope, version.TenantId);
        Assert.Equal(scope, layout!.TenantId);
    }

    [Fact]
    public async Task PromoteDraft_replay_returns_the_committed_version_after_the_source_draft_is_discarded_without_relocking()
    {
        var draftId = await CreateCommand().Execute(
            NextKey(),
            "definition-1",
            EmptyState(),
            cancellationToken: CancellationToken.None);
        var promote = PromoteCommand();
        var operationKey = NextKey();
        var versionId = await promote.Execute(operationKey, draftId, CancellationToken.None);
        var discard = new GroundworkDiscardDraftCommand(
            _locks,
            _store,
            AtomicWrite(),
            Payloads,
            _events,
            _accessContext);
        await discard.Execute(NextKey(), draftId, CancellationToken.None);
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        var definitionLockKey = WorkflowDesignPersistenceLockKeys.DefinitionKey("definition-1");
        var draftLockCount = _locks.AcquireCounts[draftLockKey];
        var definitionLockCount = _locks.AcquireCounts[definitionLockKey];
        var saveCount = _store.SaveCount;

        var replayedVersionId = await promote.Execute(operationKey, draftId, CancellationToken.None);

        Assert.Equal(versionId, replayedVersionId);
        Assert.Equal(saveCount, _store.SaveCount);
        Assert.Equal(draftLockCount, _locks.AcquireCounts[draftLockKey]);
        Assert.Equal(definitionLockCount, _locks.AcquireCounts[definitionLockKey]);
    }

    [Fact]
    public async Task DiscardDraft_deletes_the_embedded_draft_document()
    {
        var draftId = await CreateCommand().Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var discard = new GroundworkDiscardDraftCommand(_locks, _store, AtomicWrite(), Payloads, _events, _accessContext);

        await discard.Execute(NextKey(), draftId, CancellationToken.None);

        Assert.Null(await DraftStore().FindByIdAsync(draftId));
    }

    [Fact]
    public async Task SaveWorkflowDefinition_updates_definition_document()
    {
        var definition = new WorkflowDefinition { Id = "definition-1", Name = "Original" };
        await MaterializeDefinition(definition);
        var command = new GroundworkSaveWorkflowDefinitionCommand(_store, AtomicWrite(), _clock, _accessContext);
        definition.Name = "Updated";
        var createdAt = definition.CreatedAt;
        _clock.Advance();

        await command.Execute(NextKey(), definition, CancellationToken.None);

        var read = await new GroundworkWorkflowDefinitionStore(_store).GetAsync("definition-1");
        Assert.Equal("Updated", read.Name);
        Assert.Equal(createdAt, read.CreatedAt);
        Assert.True(read.LastModifiedAt > read.CreatedAt);
    }

    [Fact]
    public async Task SaveWorkflowDefinition_rejects_explicit_wrong_tenant_before_lookup_or_staging()
    {
        var command = new GroundworkSaveWorkflowDefinitionCommand(
            _store,
            AtomicWrite(),
            _clock,
            GroundworkTestAccess.AccessContext("tenant-a"));
        var definition = new WorkflowDefinition
        {
            Id = "definition-wrong-scope",
            Name = "Do not save",
            TenantId = "tenant-b"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Execute(NextKey(), definition, CancellationToken.None));

        Assert.Equal(0, _store.LoadCount);
        Assert.Equal(0, _store.BeginCount);
        Assert.Empty(_store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind));
    }

    [Fact]
    public async Task DeleteWorkflowDefinitionPermanently_removes_definition_draft_versions_and_layouts()
    {
        var createDraft = CreateCommand();
        await MaterializeDefinition(new WorkflowDefinition { Id = "definition-1", Name = "Delete me", DeletedAt = _clock.UtcNow });
        var draftId = await createDraft.Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var versionStore = VersionStore();
        var promote = PromoteCommand();
        var draft = await DraftStore().FindByWorkflowDefinitionIdAsync("definition-1");
        var versionId = await promote.Execute(NextKey(), draft!.Id, CancellationToken.None);
        _clock.Advance();
        var secondDraftId = await createDraft.Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            _store,
            AtomicWrite(),
            Payloads,
            new GroundworkWorkflowDefinitionStore(_store),
            DraftStore(),
            versionStore,
            VersionLayoutStore(),
            _accessContext);

        await delete.Execute(NextKey(), "definition-1", CancellationToken.None);

        Assert.Null(await new GroundworkWorkflowDefinitionStore(_store).FindByIdAsync("definition-1"));
        Assert.Null(await DraftStore().FindByWorkflowDefinitionIdAsync("definition-1"));
        Assert.Empty(await DraftStore().ListByWorkflowDefinitionIdAsync("definition-1"));
        Assert.NotEqual(draftId, secondDraftId);
        Assert.Null(await versionStore.FindByIdAsync(versionId));
        Assert.Null(await VersionLayoutStore().FindByVersionIdAsync(versionId));
    }

    [Fact]
    public async Task DeleteWorkflowDefinitionPermanently_rejects_an_active_definition()
    {
        var definitions = new GroundworkWorkflowDefinitionStore(_store);
        await MaterializeDefinition(new WorkflowDefinition { Id = "definition-active", Name = "Keep me" });
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            _store,
            AtomicWrite(),
            Payloads,
            definitions,
            DraftStore(),
            VersionStore(),
            VersionLayoutStore(),
            _accessContext);

        var exception = await Assert.ThrowsAsync<WorkflowDefinitionNotSoftDeletedException>(() =>
            delete.Execute(NextKey(), "definition-active", CancellationToken.None));

        Assert.Equal("definition-active", exception.DefinitionId);
        Assert.NotNull(await definitions.FindByIdAsync("definition-active"));
    }

    [Fact]
    public async Task DeleteWorkflowDefinitionPermanently_rejects_an_unknown_definition_as_not_found()
    {
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            _store,
            AtomicWrite(),
            Payloads,
            new GroundworkWorkflowDefinitionStore(_store),
            DraftStore(),
            VersionStore(),
            VersionLayoutStore(),
            _accessContext);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            delete.Execute(NextKey(), "definition-missing", CancellationToken.None));

        Assert.Contains("definition-missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteWorkflowDefinitionPermanently_refuses_when_a_deletion_guard_vetoes()
    {
        var definitions = new GroundworkWorkflowDefinitionStore(_store);
        await MaterializeDefinition(new WorkflowDefinition { Id = "definition-published", Name = "Still live", DeletedAt = _clock.UtcNow });
        var guard = new VetoingDeletionGuard();
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            _store,
            AtomicWrite(),
            Payloads,
            definitions,
            DraftStore(),
            VersionStore(),
            VersionLayoutStore(),
            _accessContext,
            [guard]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            delete.Execute(NextKey(), "definition-published", CancellationToken.None));

        Assert.Equal("definition-published", guard.SeenDefinitionId);
        Assert.NotNull(await definitions.FindByIdAsync("definition-published"));
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_returns_latest_modified_draft()
    {
        var createDraft = CreateCommand();
        var firstDraftId = await createDraft.Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        _clock.Advance();
        var secondDraftId = await createDraft.Execute(NextKey(), "definition-1", EmptyState(), cancellationToken: CancellationToken.None);

        var current = await DraftStore().FindByWorkflowDefinitionIdAsync("definition-1");

        Assert.NotNull(current);
        Assert.Equal(secondDraftId, current!.Id);
        Assert.NotEqual(firstDraftId, current.Id);
    }

    [Fact]
    public async Task SubmitWorkflowDefinition_stamps_created_entities_and_preserves_scope_identity()
    {
        var command = new GroundworkSubmitWorkflowDefinitionCommand(
            _identities,
            _store,
            AtomicWrite(),
            Payloads,
            new EmptyActivityStructureService(),
            _clock,
            _accessContext);

        var submitted = await command.Execute(NextKey(), "Definition", null, MinimalState(), CancellationToken.None);

        var definition = await new GroundworkWorkflowDefinitionStore(_store).GetAsync(submitted.DefinitionId);
        var draft = await DraftStore().FindByIdAsync(submitted.DraftId);
        var version = await VersionStore().GetAsync(submitted.VersionId);
        var layout = await VersionLayoutStore().FindByVersionIdAsync(submitted.VersionId);

        AssertStamped(definition);
        AssertStamped(draft!);
        AssertStamped(version);
        AssertStamped(layout!);
        Assert.Equal(GroundworkTestAccess.DefaultScopeValue, definition.TenantId);
        Assert.Equal(GroundworkTestAccess.DefaultScopeValue, draft!.TenantId);
        Assert.Equal(GroundworkTestAccess.DefaultScopeValue, version.TenantId);
        Assert.Equal(GroundworkTestAccess.DefaultScopeValue, layout!.TenantId);
    }

    /// <summary>
    /// Pins the published claim <c>design.submit-by-name-creates-a-new-definition</c>. Names are not
    /// identities here: submitting the same name twice yields two definitions, not a second version of the
    /// first. It is the reason a second run of an authoring script fails where the first succeeded — the new
    /// definition collides with the HTTP route the first one already claimed.
    /// </summary>
    [Fact]
    [ConsumerContract("design.submit-by-name-creates-a-new-definition")]
    public async Task SubmitWorkflowDefinition_with_an_existing_name_creates_a_second_definition()
    {
        var command = new GroundworkSubmitWorkflowDefinitionCommand(
            _identities,
            _store,
            AtomicWrite(),
            Payloads,
            new EmptyActivityStructureService(),
            _clock,
            _accessContext);

        var first = await command.Execute(NextKey(), "Same Name", null, MinimalState(), CancellationToken.None);
        var second = await command.Execute(NextKey(), "Same Name", null, MinimalState(), CancellationToken.None);

        Assert.NotEqual(first.DefinitionId, second.DefinitionId);
        Assert.NotEqual(first.VersionId, second.VersionId);

        // Both are first versions of their own definition — not v1 and v2 of one.
        var definitions = new GroundworkWorkflowDefinitionStore(_store);
        Assert.Equal("Same Name", (await definitions.GetAsync(first.DefinitionId)).Name);
        Assert.Equal("Same Name", (await definitions.GetAsync(second.DefinitionId)).Name);
    }

    [Fact]
    public async Task SubmitWorkflowDefinition_replay_skips_validation_against_changed_activity_structure()
    {
        var activityStructure = new MutableActivityStructureService();
        var command = new GroundworkSubmitWorkflowDefinitionCommand(
            _identities,
            _store,
            AtomicWrite(),
            Payloads,
            activityStructure,
            _clock,
            _accessContext);
        var operationKey = NextKey();
        var state = MinimalState();
        var submitted = await command.Execute(
            operationKey,
            "Definition",
            "Description",
            state,
            CancellationToken.None);
        var saveCount = _store.SaveCount;
        var validationProjectionCount = activityStructure.ProjectionCount;
        activityStructure.InvalidChild = new ActivityNode("child", "", [], []);

        var replayed = await command.Execute(
            operationKey,
            "Definition",
            "Description",
            state,
            CancellationToken.None);

        Assert.Equal(submitted, replayed);
        Assert.Equal(saveCount, _store.SaveCount);
        Assert.Equal(validationProjectionCount, activityStructure.ProjectionCount);
    }

    private static WorkflowDefinitionState EmptyState() => new([], null, [], [], null);

    private static WorkflowDefinitionState MinimalState() => new(
        [],
        new ActivityNode("root", "activity-version-1", [], []),
        [],
        [],
        null);

    private static void AssertStamped(Elsa.Primitives.Entities.Entity entity)
    {
        Assert.NotEqual(default, entity.CreatedAt);
        Assert.Equal(entity.CreatedAt, entity.LastModifiedAt);
    }

    private sealed class VetoingDeletionGuard : IWorkflowDefinitionPermanentDeletionGuard
    {
        public string? SeenDefinitionId { get; private set; }

        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            SeenDefinitionId = definitionId;
            throw new InvalidOperationException("Definition is still published.");
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;

        public int GenerateCount => Volatile.Read(ref _next);

        public string Generate() => $"id-{Interlocked.Increment(ref _next)}";
    }

    private sealed class FakeSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance() => UtcNow = UtcNow.AddMinutes(1);
    }

    private sealed class InMemoryLockProvider : IDistributedLockProvider
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        public ConcurrentDictionary<string, int> AcquireCounts { get; } = new();

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var semaphore = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            var acquired = semaphore.Wait(timeout ?? TimeSpan.Zero, cancellationToken);
            if (!acquired)
                return null;

            Count(name);
            return new Handle(semaphore);
        }

        public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var semaphore = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            var acquired = await semaphore.WaitAsync(timeout ?? TimeSpan.Zero, cancellationToken);
            if (!acquired)
                return null;

            Count(name);
            return new Handle(semaphore);
        }

        public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var semaphore = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            Count(name);
            return new Handle(semaphore);
        }

        private void Count(string name) => AcquireCounts.AddOrUpdate(name, 1, (_, count) => count + 1);

        private sealed class Handle(SemaphoreSlim semaphore) : IDistributedSynchronizationHandle
        {
            private int _disposed;

            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    semaphore.Release();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class MutableActivityStructureService : IActivityStructureService
    {
        public ActivityNode? InvalidChild { get; set; }
        public int ProjectionCount { get; private set; }

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            ProjectionCount++;
            return InvalidChild is null || activity.NodeId != "root"
                ? []
                : [new ActivityChildProjection("Body", [InvalidChild])];
        }

        public ActivityNode ReplaceChildren(
            ActivityNode activity,
            IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;

        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class AcknowledgementLostAfterCommitDocumentStore(
        IDocumentStore inner,
        CancellationTokenSource callerCancellation) : IDocumentStore
    {
        private int _ledgerLoads;

        public bool ReconciliationUsedFreshToken { get; private set; }
        public global::Groundwork.Documents.Scoping.DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            if (documentKind == GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind &&
                Interlocked.Increment(ref _ledgerLoads) > 1)
            {
                ReconciliationUsedFreshToken = !cancellationToken.IsCancellationRequested;
            }

            return inner.LoadAsync(documentKind, id, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<global::Groundwork.Documents.Store.DocumentQueryResult> QueryAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new AcknowledgementLostAfterCommitUnitOfWork(
                await inner.BeginAsync(scope, cancellationToken),
                callerCancellation);
    }

    private sealed class VersionSaveConflictDocumentStore(IDocumentStore inner) : IDocumentStore, IBoundedDocumentStore
    {
        public bool VersionSaveWasRejected { get; set; }
        public global::Groundwork.Documents.Scoping.DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<global::Groundwork.Documents.Store.DocumentQueryResult> QueryAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            ((IBoundedDocumentStore)inner).QueryAsync(query, cancellationToken);

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            ((IBoundedDocumentStore)inner).CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            ((IBoundedDocumentStore)inner).FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            ((IBoundedDocumentStore)inner).AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new VersionSaveConflictUnitOfWork(
                await inner.BeginAsync(scope, cancellationToken),
                this);
    }

    private sealed class VersionSaveConflictUnitOfWork(
        IDocumentUnitOfWork inner,
        VersionSaveConflictDocumentStore owner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.DocumentKind ==
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind)
            {
                owner.VersionSaveWasRejected = true;
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            }

            return inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class AcknowledgementLostAfterCommitUnitOfWork(
        IDocumentUnitOfWork inner,
        CancellationTokenSource callerCancellation) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(cancellationToken);
            await callerCancellation.CancelAsync();
            throw new DocumentCommitAcknowledgementUncertainException(
                [GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind]);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
