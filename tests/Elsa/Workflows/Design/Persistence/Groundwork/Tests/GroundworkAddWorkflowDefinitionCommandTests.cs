using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Store;
using Xunit;
using Elsa.Primitives.Exceptions;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkAddWorkflowDefinitionCommand"/> atomically writes the
/// workflow definition and its first draft into one document store and that both read back through the matching
/// read ports — the document-store counterpart of the EF Core add command's single <c>SaveChangesAsync</c>.
/// </summary>
public class GroundworkAddWorkflowDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    private static GroundworkDesignStorage Storage(
        IGroundworkStorageSessionSource source,
        IPersistenceAccessContextAccessor? access = null) =>
        new(source, access ?? GroundworkTestAccess.DefaultAccessContextAccessor);

    private static GroundworkDesignAtomicWrite Atomic(
        IGroundworkStorageSessionSource source,
        IPersistenceAccessContextAccessor? access = null) =>
        new(Storage(source, access));

    [Fact]
    public async Task Mismatched_draft_tenant_rejects_the_complete_batch_before_staging()
    {
        using var store = new DesignGroundworkTestPersistence();
        var command = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store, GroundworkTestAccess.AccessContext("tenant-a")),
            Atomic(store, GroundworkTestAccess.AccessContext("tenant-a")),
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.AccessContext("tenant-a"));
        var definition = new WorkflowDefinition { Id = "def-1", Name = "Onboarding", TenantId = "tenant-a" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
            TenantId = "tenant-b",
            State = WorkflowDefinitionState.Empty
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(new DesignOperationKey("request-1"), definition, draft, [], CancellationToken.None));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind));
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_definition_outside_the_current_tenant_before_staging()
    {
        using var store = new DesignGroundworkTestPersistence();
        var seed = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store, GroundworkTestAccess.AccessContext("tenant-b")),
            Atomic(store, GroundworkTestAccess.AccessContext("tenant-b")),
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.AccessContext("tenant-b"));
        await seed.Execute(
            new DesignOperationKey("seed"),
            new WorkflowDefinition { Id = "def-1", Name = "Definition", TenantId = "tenant-b" },
            new WorkflowDefinitionDraft
            {
                Id = "draft-1",
                WorkflowDefinitionId = "def-1",
                TenantId = "tenant-b",
                State = WorkflowDefinitionState.Empty
            },
            [],
            CancellationToken.None);
        var definitions = new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor);
        var command = new GroundworkAddWorkflowDefinitionVersionCommand(
            Atomic(store),
            Payloads,
            new WorkflowDefinitionVersionFactory(new SequentialIdentityGenerator()),
            definitions,
            new GroundworkWorkflowDefinitionVersionStore(store, definitions, Payloads, GroundworkTestAccess.DefaultAccessContextAccessor),
            new ImmediateLockProvider(),
            new FakeSystemClock(),
            GroundworkTestAccess.AccessContext("tenant-a"));
        var savesBeforeAttempt = store.SaveCount;

        // A definition outside the current tenant is invisible to the scope-bound store, so this is a
        // not-found -- which WorkflowsDesignApi answers as 404. It must not surface as a server error.
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            command.Execute(new DesignOperationKey("request-1"), "def-1", WorkflowDefinitionState.Empty));

        Assert.Equal(savesBeforeAttempt, store.SaveCount);
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_replays_the_authoritative_identity_and_rejects_changed_state()
    {
        using var store = new DesignGroundworkTestPersistence();
        var atomicWrite = Atomic(store);
        var definitions = new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor);
        var seed = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store),
            atomicWrite,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        await seed.Execute(
            new DesignOperationKey("seed"),
            new WorkflowDefinition { Id = "def-1", Name = "Definition" },
            new WorkflowDefinitionDraft
            {
                Id = "draft-1",
                WorkflowDefinitionId = "def-1",
                State = WorkflowDefinitionState.Empty
            },
            [],
            CancellationToken.None);
        var command = new GroundworkAddWorkflowDefinitionVersionCommand(
            atomicWrite,
            Payloads,
            new WorkflowDefinitionVersionFactory(new SequentialIdentityGenerator()),
            definitions,
            new GroundworkWorkflowDefinitionVersionStore(store, definitions, Payloads, GroundworkTestAccess.DefaultAccessContextAccessor),
            new ImmediateLockProvider(),
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var key = new DesignOperationKey("request-1");

        var first = await command.Execute(key, "def-1", WorkflowDefinitionState.Empty);
        var savesAfterFirst = store.SaveCount;
        var replay = await command.Execute(key, "def-1", WorkflowDefinitionState.Empty);

        Assert.Equal(first, replay);
        Assert.Equal("1.0.0", replay.Version);
        Assert.Equal(savesAfterFirst, store.SaveCount);
        Assert.Single(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind));

        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() =>
            command.Execute(
                key,
                "def-1",
                new WorkflowDefinitionState([], new ActivityNode("root", "activity", [], []), [], [], null)));
        Assert.Equal(savesAfterFirst, store.SaveCount);
        Assert.Single(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_race_computed_duplicate_version_without_persisting()
    {
        // Deterministic replay of issue #404: by the time this publish computed "2.0.0" from its
        // stale latest-version read, a concurrent publish with a distinct operation key had already
        // persisted "2.0.0" — the in-lock existence guard must reject rather than silently persist
        // a duplicate computed version.
        using var store = new DesignGroundworkTestPersistence();
        var atomicWrite = Atomic(store);
        var definitions = new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor);
        var versions = new GroundworkWorkflowDefinitionVersionStore(store, definitions, Payloads, GroundworkTestAccess.DefaultAccessContextAccessor);
        var seed = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store),
            atomicWrite,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        await seed.Execute(
            new DesignOperationKey("seed"),
            new WorkflowDefinition { Id = "def-1", Name = "Definition" },
            new WorkflowDefinitionDraft
            {
                Id = "draft-1",
                WorkflowDefinitionId = "def-1",
                State = WorkflowDefinitionState.Empty
            },
            [],
            CancellationToken.None);
        var versionFactory = new WorkflowDefinitionVersionFactory(new SequentialIdentityGenerator());
        GroundworkAddWorkflowDefinitionVersionCommand CreateCommand(IWorkflowDefinitionVersionStore versionStore) =>
            new(
                atomicWrite,
                Payloads,
                versionFactory,
                definitions,
                versionStore,
                new ImmediateLockProvider(),
                new FakeSystemClock(),
                GroundworkTestAccess.DefaultAccessContextAccessor);

        var first = await CreateCommand(versions)
            .Execute(new DesignOperationKey("publish-1"), "def-1", WorkflowDefinitionState.Empty);
        var second = await CreateCommand(versions)
            .Execute(new DesignOperationKey("publish-2"), "def-1", WorkflowDefinitionState.Empty);
        var staleLatest = await versions.GetAsync(first.VersionId);
        var racingCommand = CreateCommand(new StaleLatestVersionStore(versions, staleLatest));
        var savesBeforeRace = store.SaveCount;

        var conflict = await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() =>
            racingCommand.Execute(new DesignOperationKey("publish-race"), "def-1", WorkflowDefinitionState.Empty));

        Assert.Equal("def-1", conflict.DefinitionId);
        Assert.Equal(second.Version, conflict.Version);
        Assert.Equal(savesBeforeRace, store.SaveCount);
        Assert.Equal(2, store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind).Count);
    }

    /*
     * The API command allocates version identities behind its operation ledger. Source-owned versions
     * use IMaterializeWorkflowDefinitionVersionCommand instead; see the reconciler tests for that path.
     */

    /// <summary>
    /// Presents the race window deterministically: the latest-version read is stale while the
    /// existence check sees the concurrently committed sibling, exactly as a lock-per-node or
    /// cross-node race would.
    /// </summary>
    private sealed class StaleLatestVersionStore(
        IWorkflowDefinitionVersionStore inner,
        WorkflowDefinitionVersion staleLatest) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(versionId, cancellationToken);

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(versionId, cancellationToken);

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            inner.GetWithDefinitionAsync(versionId, cancellationToken);

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(staleLatest);

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(definitionId, cancellationToken);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(definitionId, semVerSortKey, cancellationToken);
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"version-{++_next}";
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Persists_definition_and_draft_readable_through_the_ports()
    {
        using var store = new DesignGroundworkTestPersistence();
        var command = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store),
            Atomic(store),
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);

        var definition = new WorkflowDefinition { Id = "def-1", Name = "Onboarding", Description = "New hire flow" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

        var layout = new[] { new DesignMetadataRecord("node-1", 10, 20, 100, 80) };
        var result = await command.Execute(
            new DesignOperationKey("request-1"),
            definition,
            draft,
            layout,
            CancellationToken.None);

        var definitionStore = new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor);
        var draftStore = new GroundworkWorkflowDefinitionDraftStore(
            Storage(store),
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor);

        var readDefinition = await definitionStore.FindByIdAsync("def-1");
        var readDraft = await draftStore.FindByWorkflowDefinitionIdAsync("def-1");
        var readLayout = await draftStore.FindLayoutByDraftIdAsync("draft-1");

        Assert.NotNull(readDefinition);
        Assert.Equal("Onboarding", readDefinition!.Name);
        Assert.NotNull(readDraft);
        Assert.Equal("draft-1", readDraft!.Id);
        Assert.Equal(layout, readLayout);
        Assert.Equal("def-1", result.DefinitionId);
        Assert.Equal("draft-1", result.DraftId);
    }

    [Fact]
    public async Task Exact_replay_returns_the_first_authoritative_ids_without_restaging()
    {
        using var store = new DesignGroundworkTestPersistence();
        var command = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store),
            Atomic(store),
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var key = new DesignOperationKey("request-1");

        var first = await command.Execute(
            key,
            Definition("def-first", "Onboarding"),
            Draft("draft-first", "def-first"),
            [],
            CancellationToken.None);
        var savesAfterFirstAttempt = store.SaveCount;

        var replay = await command.Execute(
            key,
            Definition("def-retry", "Onboarding"),
            Draft("draft-retry", "def-retry"),
            [],
            CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal("def-first", replay.DefinitionId);
        Assert.Equal("draft-first", replay.DraftId);
        Assert.Equal(savesAfterFirstAttempt, store.SaveCount);
        Assert.Null(await new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor).FindByIdAsync("def-retry"));
    }

    [Fact]
    public async Task Reusing_the_key_for_changed_material_conflicts_without_mutation()
    {
        using var store = new DesignGroundworkTestPersistence();
        var command = new GroundworkAddWorkflowDefinitionCommand(
            Storage(store),
            Atomic(store),
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var key = new DesignOperationKey("request-1");

        await command.Execute(
            key,
            Definition("def-first", "Onboarding"),
            Draft("draft-first", "def-first"),
            [],
            CancellationToken.None);
        var savesAfterFirstAttempt = store.SaveCount;

        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() =>
            command.Execute(
                key,
                Definition("def-retry", "Changed"),
                Draft("draft-retry", "def-retry"),
                [],
                CancellationToken.None));

        Assert.Equal(savesAfterFirstAttempt, store.SaveCount);
        Assert.Null(await new GroundworkWorkflowDefinitionStore(store, GroundworkTestAccess.DefaultAccessContextAccessor).FindByIdAsync("def-retry"));
    }

    private static WorkflowDefinition Definition(string id, string name) =>
        new() { Id = id, Name = name };

    private static WorkflowDefinitionDraft Draft(string id, string definitionId) =>
        new()
        {
            Id = id,
            WorkflowDefinitionId = definitionId,
            State = WorkflowDefinitionState.Empty
        };

    private sealed class FakeSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
