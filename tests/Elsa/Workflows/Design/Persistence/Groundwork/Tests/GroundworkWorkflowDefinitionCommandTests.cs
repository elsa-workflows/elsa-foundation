using System.Collections.Concurrent;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public class GroundworkWorkflowDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();
    private readonly FakeSystemClock _clock = new();

    [Fact]
    public async Task CreateDraft_persists_layout_and_validation_errors_in_the_draft_document()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var locks = new InMemoryLockProvider();
        var events = new CapturingEventPublisher
        {
            OnPublish = e =>
            {
                if (e is OnDraftValidating validating)
                    validating.Errors.Add(new ValidationError("$workflow", "Test/Error", "Invalid"));
            }
        };
        var command = new GroundworkCreateDraftCommand(new SequentialIdentityGenerator(), locks, store, Payloads, events, _clock);
        var layout = new[] { new DesignMetadataRecord("root", 10, 20, 300, 200) };

        var draftId = await command.Execute("definition-1", EmptyState(), layout, cancellationToken: CancellationToken.None);

        var draftStore = new GroundworkWorkflowDefinitionDraftStore(store, Payloads);
        var draft = await draftStore.FindByIdAsync(draftId);
        var readLayout = await draftStore.FindLayoutByDraftIdAsync(draftId);
        var errors = await draftStore.FindValidationErrorsByDraftIdAsync(draftId);

        Assert.NotNull(draft);
        Assert.Equal("definition-1", draft!.WorkflowDefinitionId);
        Assert.Equal(layout.Single(), readLayout.Single());
        Assert.Equal("Test/Error", errors.Single().Type);
        Assert.Equal(1, locks.AcquireCounts[WorkflowDesignPersistenceLockKeys.DraftKey(draftId)]);
    }

    [Fact]
    public async Task UpdateDraft_replaces_layout_and_validation_errors_atomically()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var locks = new InMemoryLockProvider();
        var events = new CapturingEventPublisher();
        var create = new GroundworkCreateDraftCommand(new SequentialIdentityGenerator(), locks, store, Payloads, events, _clock);
        var draftId = await create.Execute("definition-1", EmptyState(), [new DesignMetadataRecord("old", 0, 0, 100, 100)], cancellationToken: CancellationToken.None);
        events.OnPublish = e =>
        {
            if (e is OnDraftValidating validating)
                validating.Errors.Add(new ValidationError("root", "Updated/Error", "Still invalid"));
        };
        var update = new GroundworkUpdateDraftCommand(
            locks,
            store,
            Payloads,
            events,
            new DraftStateDiffEngine(new EmptyActivityStructureService()),
            _clock);
        var nextLayout = new[] { new DesignMetadataRecord("root", 1, 2, 3, 4) };

        await update.Execute(new UpdateDraftRequest(draftId, EmptyState(), nextLayout), CancellationToken.None);

        var draftStore = new GroundworkWorkflowDefinitionDraftStore(store, Payloads);
        var readLayout = await draftStore.FindLayoutByDraftIdAsync(draftId);
        var errors = await draftStore.FindValidationErrorsByDraftIdAsync(draftId);

        Assert.Equal(nextLayout.Single(), readLayout.Single());
        Assert.Equal("Updated/Error", errors.Single().Type);
    }

    [Fact]
    public async Task PromoteDraft_rejects_validation_errors_and_promotes_clean_drafts_with_layout()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var locks = new InMemoryLockProvider();
        var identities = new SequentialIdentityGenerator();
        var events = new CapturingEventPublisher
        {
            OnPublish = e =>
            {
                if (e is OnDraftValidating validating)
                    validating.Errors.Add(new ValidationError("$workflow", "Blocking/Error", "Nope"));
            }
        };
        var create = new GroundworkCreateDraftCommand(identities, locks, store, Payloads, events, _clock);
        var blockedDraftId = await create.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var versionStore = new GroundworkWorkflowDefinitionVersionStore(store, new GroundworkWorkflowDefinitionStore(store), Payloads);
        var promote = new GroundworkPromoteDraftToVersionCommand(locks, store, Payloads, versionStore, identities, _clock);

        await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => promote.Execute(blockedDraftId, CancellationToken.None));

        events.OnPublish = null;
        var layout = new[] { new DesignMetadataRecord("root", 5, 6, 7, 8) };
        var cleanDraftId = await create.Execute("definition-1", EmptyState(), layout, cancellationToken: CancellationToken.None);

        var versionId = await promote.Execute(cleanDraftId, CancellationToken.None);

        var version = await versionStore.GetAsync(versionId);
        var versionLayout = await new GroundworkWorkflowDefinitionVersionLayoutStore(store)
            .FindByVersionIdAsync(versionId);

        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(layout.Single(), versionLayout!.Records.Single());
    }

    [Fact]
    public async Task DiscardDraft_deletes_the_embedded_draft_document()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var locks = new InMemoryLockProvider();
        var events = new CapturingEventPublisher();
        var create = new GroundworkCreateDraftCommand(new SequentialIdentityGenerator(), locks, store, Payloads, events, _clock);
        var draftId = await create.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var discard = new GroundworkDiscardDraftCommand(locks, store, Payloads, events);

        await discard.Execute(draftId, CancellationToken.None);

        var draftStore = new GroundworkWorkflowDefinitionDraftStore(store, Payloads);
        Assert.Null(await draftStore.FindByIdAsync(draftId));
    }

    [Fact]
    public async Task SaveWorkflowDefinition_updates_definition_document()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkSaveWorkflowDefinitionCommand(store, _clock);
        var definition = new WorkflowDefinition { Id = "definition-1", Name = "Original" };
        await command.Execute(definition, CancellationToken.None);
        definition.Name = "Updated";
        var createdAt = definition.CreatedAt;
        _clock.Advance();

        await command.Execute(definition, CancellationToken.None);

        var read = await new GroundworkWorkflowDefinitionStore(store).GetAsync("definition-1");
        Assert.Equal("Updated", read.Name);
        Assert.Equal(createdAt, read.CreatedAt);
        Assert.True(read.LastModifiedAt > read.CreatedAt);
    }

    [Fact]
    public async Task DeleteWorkflowDefinitionPermanently_removes_definition_draft_versions_and_layouts()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var identities = new SequentialIdentityGenerator();
        var saveDefinition = new GroundworkSaveWorkflowDefinitionCommand(store, _clock);
        var createDraft = new GroundworkCreateDraftCommand(
            identities,
            new InMemoryLockProvider(),
            store,
            Payloads,
            new CapturingEventPublisher(),
            _clock);
        await saveDefinition.Execute(new WorkflowDefinition { Id = "definition-1", Name = "Delete me" }, CancellationToken.None);
        var draftId = await createDraft.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var versionStore = new GroundworkWorkflowDefinitionVersionStore(store, new GroundworkWorkflowDefinitionStore(store), Payloads);
        var promote = new GroundworkPromoteDraftToVersionCommand(
            new InMemoryLockProvider(),
            store,
            Payloads,
            versionStore,
            identities,
            _clock);
        var draft = await new GroundworkWorkflowDefinitionDraftStore(store, Payloads).FindByWorkflowDefinitionIdAsync("definition-1");
        var versionId = await promote.Execute(draft!.Id, CancellationToken.None);
        _clock.Advance();
        var secondDraftId = await createDraft.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            store,
            Payloads,
            new GroundworkWorkflowDefinitionDraftStore(store, Payloads),
            versionStore,
            new GroundworkWorkflowDefinitionVersionLayoutStore(store));

        await delete.Execute("definition-1", CancellationToken.None);

        Assert.Null(await new GroundworkWorkflowDefinitionStore(store).FindByIdAsync("definition-1"));
        Assert.Null(await new GroundworkWorkflowDefinitionDraftStore(store, Payloads).FindByWorkflowDefinitionIdAsync("definition-1"));
        Assert.Empty(await new GroundworkWorkflowDefinitionDraftStore(store, Payloads).ListByWorkflowDefinitionIdAsync("definition-1"));
        Assert.NotEqual(draftId, secondDraftId);
        Assert.Null(await versionStore.FindByIdAsync(versionId));
        Assert.Null(await new GroundworkWorkflowDefinitionVersionLayoutStore(store).FindByVersionIdAsync(versionId));
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_returns_latest_modified_draft()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var createDraft = new GroundworkCreateDraftCommand(
            new SequentialIdentityGenerator(),
            new InMemoryLockProvider(),
            store,
            Payloads,
            new CapturingEventPublisher(),
            _clock);
        var firstDraftId = await createDraft.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        _clock.Advance();
        var secondDraftId = await createDraft.Execute("definition-1", EmptyState(), cancellationToken: CancellationToken.None);
        var draftStore = new GroundworkWorkflowDefinitionDraftStore(store, Payloads);

        var current = await draftStore.FindByWorkflowDefinitionIdAsync("definition-1");

        Assert.NotNull(current);
        Assert.Equal(secondDraftId, current!.Id);
        Assert.NotEqual(firstDraftId, current.Id);
    }

    [Fact]
    public async Task SubmitWorkflowDefinition_stamps_created_entities()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkSubmitWorkflowDefinitionCommand(
            new SequentialIdentityGenerator(),
            store,
            Payloads,
            new EmptyActivityStructureService(),
            _clock);

        var submitted = await command.Execute("Definition", null, MinimalState(), CancellationToken.None);

        var definition = await new GroundworkWorkflowDefinitionStore(store).GetAsync(submitted.DefinitionId);
        var draft = await new GroundworkWorkflowDefinitionDraftStore(store, Payloads).FindByIdAsync(submitted.DraftId);
        var versionStore = new GroundworkWorkflowDefinitionVersionStore(store, new GroundworkWorkflowDefinitionStore(store), Payloads);
        var version = await versionStore.GetAsync(submitted.VersionId);
        var layout = await new GroundworkWorkflowDefinitionVersionLayoutStore(store).FindByVersionIdAsync(submitted.VersionId);

        AssertStamped(definition);
        AssertStamped(draft!);
        AssertStamped(version);
        AssertStamped(layout!);
    }

    private static WorkflowDefinitionState EmptyState() => new([], null, [], [], null, null);

    private static WorkflowDefinitionState MinimalState() => new(
        [],
        new ActivityNode("root", "activity-version-1", [], []),
        [],
        [],
        null,
        null);

    private static void AssertStamped(Elsa.Primitives.Entities.Entity entity)
    {
        Assert.NotEqual(default, entity.CreatedAt);
        Assert.Equal(entity.CreatedAt, entity.LastModifiedAt);
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"id-{Interlocked.Increment(ref _next)}";
    }

    private sealed class CapturingEventPublisher : IEventPublisher
    {
        public Action<IEvent>? OnPublish { get; set; }

        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            OnPublish?.Invoke(@event);
            return Task.CompletedTask;
        }

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
}
