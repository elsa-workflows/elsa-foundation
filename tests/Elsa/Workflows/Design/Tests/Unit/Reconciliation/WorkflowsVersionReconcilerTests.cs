using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Enums;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Branch coverage for <see cref="WorkflowsVersionReconciler"/>. Cases: fresh definition +
/// first version → both adds called; existing definition with new version number → only
/// version-add called; existing (DefId, Version) → duplicate-handling kicks in
/// (Skip default + Throw override).
/// </summary>
public sealed class WorkflowsVersionReconcilerTests
{
    [Fact]
    public async Task Fresh_definition_calls_both_add_definition_and_add_version()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-new", version: "1.0.0");
        var sender = new CapturingSender { ToContribute = [incoming] };
        var defs = new StubDefinitionStore();
        var versions = new StubVersionStore();
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();

        var reconciler = NewReconciler(sender, defs, versions, addDef, addVer, DuplicateHandling.Skip);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Single(addDef.Added);
        Assert.Equal("wf-new", addDef.Added[0].Id);
        // Materialized from a source ⇒ the source owns lifecycle metadata (DeletedAt reconciliation).
        Assert.True(addDef.Added[0].IsSourceOwned);
        Assert.Single(addVer.Added);
        Assert.Equal("wf-new", addVer.Added[0].DefinitionId);
        Assert.Equal("1.0.0", addVer.Added[0].Version);
    }

    [Fact]
    public async Task Existing_definition_new_version_calls_add_version_only()
    {
        // Incoming metadata matches the persisted definition, so only the new version is added.
        var incoming = BuildIncomingVersion(definitionId: "wf-existing", version: "2.0.0", name: "Existing");
        var existingDef = new WorkflowDefinition { Id = "wf-existing", Name = "Existing" };

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore(); // No existing versions.
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(addDef.Added);
        Assert.Empty(saveDef.Saved);
        Assert.Single(addVer.Added);
        Assert.Equal("2.0.0", addVer.Added[0].Version);
    }

    [Fact]
    public async Task Duplicate_version_with_Skip_handling_does_not_add()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-dup", version: "1.0.0");
        var existingDef = new WorkflowDefinition { Id = "wf-dup", Name = "Dup" };
        var existingVersion = new WorkflowDefinitionVersion("wf-dup", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(addDef.Added);
        Assert.Empty(addVer.Added);
    }

    [Fact]
    public async Task Changed_metadata_on_existing_definition_saves_updated_name_and_description()
    {
        // Same (definitionId, version) as the persisted one — a rename with no version change.
        var incoming = BuildIncomingVersion(definitionId: "wf-rename", version: "1.0.0", name: "New Name", description: "New description");
        var existingDef = new WorkflowDefinition { Id = "wf-rename", Name = "Old Name", Description = "Old description" };
        var existingVersion = new WorkflowDefinitionVersion("wf-rename", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(addDef.Added);
        var saved = Assert.Single(saveDef.Saved);
        Assert.Equal("wf-rename", saved.Id);
        Assert.Equal("New Name", saved.Name);
        Assert.Equal("New description", saved.Description);
        // Retention authority: a metadata-only change never touches versions.
        Assert.Empty(addVer.Added);
    }

    [Fact]
    public async Task Unchanged_metadata_on_existing_definition_writes_nothing()
    {
        // Incoming metadata is byte-for-byte the persisted metadata.
        var incoming = BuildIncomingVersion(definitionId: "wf-same", version: "1.0.0", name: "Same", description: "Same description");
        var existingDef = new WorkflowDefinition { Id = "wf-same", Name = "Same", Description = "Same description" };
        var existingVersion = new WorkflowDefinitionVersion("wf-same", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        // Idempotent: no add and no save when nothing changed.
        Assert.Empty(addDef.Added);
        Assert.Empty(saveDef.Saved);
        Assert.Empty(addVer.Added);
    }

    [Fact]
    public async Task Metadata_update_does_not_add_or_alter_versions()
    {
        // A rename arriving with a brand-new version number: the definition metadata updates, and the
        // version is added exactly once — the metadata path itself adds/alters no versions.
        var incoming = BuildIncomingVersion(definitionId: "wf-both", version: "2.0.0", name: "Renamed");
        var existingDef = new WorkflowDefinition { Id = "wf-both", Name = "Original" };
        var existingVersion = new WorkflowDefinitionVersion("wf-both", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Single(saveDef.Saved);
        Assert.Equal("Renamed", saveDef.Saved[0].Name);
        // The pre-existing 1.0.0 is untouched; only the genuinely-new 2.0.0 is added.
        var added = Assert.Single(addVer.Added);
        Assert.Equal("2.0.0", added.Version);
    }

    [Fact]
    public async Task Older_incoming_entry_does_not_change_definition_metadata()
    {
        // FR-008a: a stale/older version entry (persisted 2.0.0, incoming 1.0.0) with a rename must NOT
        // overwrite current metadata — the metadata apply is gated behind the outdated-version skip.
        var incoming = BuildIncomingVersion(definitionId: "wf-stale", version: "1.0.0", name: "Stale Name", description: "Stale");
        var existingDef = new WorkflowDefinition { Id = "wf-stale", Name = "Current Name", Description = "Current" };
        var newerVersion = new WorkflowDefinitionVersion("wf-stale", "2.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(newerVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        // Outdated entry touches nothing: no metadata save, no version add.
        Assert.Empty(saveDef.Saved);
        Assert.Empty(addDef.Added);
        Assert.Empty(addVer.Added);
    }

    [Fact]
    public async Task Incoming_deleted_soft_deletes_definition_without_deleting_versions()
    {
        // R10/FR-008: definition.json marks a source-owned definition deleted → DeletedAt set; no version row removed.
        var incoming = BuildIncomingVersion(definitionId: "wf-del", version: "1.0.0", name: "Same", deleted: true);
        var existingDef = new WorkflowDefinition { Id = "wf-del", Name = "Same", IsSourceOwned = true }; // live
        var existingVersion = new WorkflowDefinitionVersion("wf-del", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), addVer, DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        var saved = Assert.Single(saveDef.Saved);
        Assert.NotNull(saved.DeletedAt);
        Assert.Empty(addVer.Added); // retention authority: no version added or deleted
    }

    [Fact]
    public async Task Incoming_live_undeletes_soft_deleted_definition()
    {
        // R10: definition.json latest-wins — a source reporting a source-owned definition live clears DeletedAt.
        var incoming = BuildIncomingVersion(definitionId: "wf-undel", version: "1.0.0", name: "Same", deleted: false);
        var existingDef = new WorkflowDefinition { Id = "wf-undel", Name = "Same", DeletedAt = DateTimeOffset.UtcNow.AddDays(-1), IsSourceOwned = true };
        var existingVersion = new WorkflowDefinitionVersion("wf-undel", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(), DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        var saved = Assert.Single(saveDef.Saved);
        Assert.Null(saved.DeletedAt);
    }

    [Fact]
    public async Task Incoming_deleted_does_not_soft_delete_non_source_owned_definition()
    {
        // Guard: a source reporting a catalog-authored (non-source-owned) definition as deleted must NOT
        // flip DeletedAt — latest-wins soft-delete is scoped to definitions the source materialized itself.
        var incoming = BuildIncomingVersion(definitionId: "wf-studio", version: "1.0.0", name: "Same", deleted: true);
        var existingDef = new WorkflowDefinition { Id = "wf-studio", Name = "Same" }; // Studio-authored, live
        var existingVersion = new WorkflowDefinitionVersion("wf-studio", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var saveDef = new SpySaveDefinitionCommand();
        var logger = new CapturingLogger<WorkflowsVersionReconciler>();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Skip, saveDef, logger);
        await reconciler.Reconcile(CancellationToken.None);

        // Nothing else changed, so the ignored delete intent results in no write at all.
        Assert.Empty(saveDef.Saved);
        Assert.Null(existingDef.DeletedAt);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not source-owned"));
    }

    [Fact]
    public async Task Incoming_live_does_not_undelete_non_source_owned_definition()
    {
        // Guard, other direction: a source may not resurrect a catalog-authored soft-deleted definition.
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var incoming = BuildIncomingVersion(definitionId: "wf-studio-del", version: "1.0.0", name: "Same", deleted: false);
        var existingDef = new WorkflowDefinition { Id = "wf-studio-del", Name = "Same", DeletedAt = deletedAt };
        var existingVersion = new WorkflowDefinitionVersion("wf-studio-del", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(saveDef.Saved);
        Assert.Equal(deletedAt, existingDef.DeletedAt);
    }

    [Fact]
    public async Task Rename_still_applies_when_delete_intent_is_ignored_on_non_source_owned_definition()
    {
        // The ownership guard scopes only DeletedAt: name/description stay latest-wins for every source.
        var incoming = BuildIncomingVersion(definitionId: "wf-studio-ren", version: "1.0.0", name: "New Name", deleted: true);
        var existingDef = new WorkflowDefinition { Id = "wf-studio-ren", Name = "Old Name" };
        var existingVersion = new WorkflowDefinitionVersion("wf-studio-ren", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var saveDef = new SpySaveDefinitionCommand();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Skip, saveDef);
        await reconciler.Reconcile(CancellationToken.None);

        var saved = Assert.Single(saveDef.Saved);
        Assert.Equal("New Name", saved.Name);
        Assert.Null(saved.DeletedAt);
    }

    [Fact]
    public async Task Same_id_version_different_content_logs_warning()
    {
        // R13/FR-006: same (id, version) with different canonical state is a broken source — surfaced as
        // a warning now (a throw arrives once FR-016a persists a hash). Under Skip, no throw, no add.
        var incomingState = new WorkflowDefinitionState([], null, [], [], null);
        var storedState = new WorkflowDefinitionState([], null, [], [], null);
        var incoming = BuildIncomingVersion(definitionId: "wf-diff", version: "1.0.0", name: "Same", state: incomingState);
        var existingDef = new WorkflowDefinition { Id = "wf-diff", Name = "Same" };
        var existingVersion = new WorkflowDefinitionVersion("wf-diff", "1.0.0") { State = storedState };

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);
        var logger = new CapturingLogger<WorkflowsVersionReconciler>();
        var serializer = new FakePayloadSerializer().With(incomingState, "A").With(storedState, "B");

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Skip, logger: logger, serializer: serializer);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("different content"));
    }

    [Fact]
    public async Task Duplicate_version_with_Throw_handling_throws()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-dup", version: "1.0.0");
        var existingDef = new WorkflowDefinition { Id = "wf-dup", Name = "Dup" };
        var existingVersion = new WorkflowDefinitionVersion("wf-dup", "1.0.0");

        var defs = new StubDefinitionStore().With(existingDef);
        var versions = new StubVersionStore().With(existingVersion);

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions,
            new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Throw);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reconciler.Reconcile(CancellationToken.None));
    }

    private static WorkflowsVersionReconciler NewReconciler(
        IInlineEventPublisher sender,
        IWorkflowDefinitionStore defs,
        IWorkflowDefinitionVersionStore versions,
        IAddCommand<WorkflowDefinition> addDef,
        IAddCommand<WorkflowDefinitionVersion> addVer,
        DuplicateHandling duplicateHandling,
        ISaveWorkflowDefinitionCommand? saveDef = null,
        ILogger<WorkflowsVersionReconciler>? logger = null,
        IPayloadSerializer? serializer = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions { DuplicateHandling = duplicateHandling });
        return new WorkflowsVersionReconciler(
            logger ?? NullLogger<WorkflowsVersionReconciler>.Instance,
            sender,
            options,
            defs,
            versions,
            addDef,
            addVer,
            saveDef ?? new SpySaveDefinitionCommand(),
            serializer ?? new FakePayloadSerializer());
    }

    private static IWorkflowDefinitionVersion BuildIncomingVersion(
        string definitionId, string version, string name = "Stub", string? description = null,
        bool deleted = false, WorkflowDefinitionState? state = null) => new StubIncomingVersion
    {
        Id = string.Empty,
        Version = version,
        DefinitionId = definitionId,
        DefinitionFacade = new StubIncomingDefinition { Id = definitionId, Name = name, Description = description, DeletedAt = deleted ? DateTimeOffset.UtcNow : null },
        State = state ?? new WorkflowDefinitionState([], null, [], [], null),
    };

    private sealed class StubIncomingDefinition : IWorkflowDefinition
    {
        public string Id { get; init; } = default!;
        public string Name { get; init; } = default!;
        public string? Description { get; init; }
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset? DeletedAt { get; init; }
        public IWorkflowDefinition ShallowClone() => this;
    }

    private sealed class StubIncomingVersion : IWorkflowDefinitionVersion
    {
        public string Id { get; init; } = default!;
        public string Version { get; init; } = default!;
        public string DefinitionId { get; init; } = default!;
        public StubIncomingDefinition DefinitionFacade { get; init; } = default!;
        public IWorkflowDefinition Definition => DefinitionFacade;
        public WorkflowDefinitionState State { get; init; } = default!;
        public string? StateSource => null;
        public DateTimeOffset? SourceCreatedAt => null;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Minimal <see cref="IPayloadSerializer"/> for the reconciler's content-mismatch tripwire: only
    /// <see cref="Serialize(object)"/> is exercised. A reference-keyed map lets a test assign a canonical
    /// string per state instance (so identical content ⇒ equal string, differing content ⇒ different
    /// string) without constructing domain state; unmapped payloads fall back to real JSON.
    /// </summary>
    private sealed class FakePayloadSerializer : IPayloadSerializer
    {
        private readonly Dictionary<object, string> _map = new(ReferenceEqualityComparer.Instance);
        public FakePayloadSerializer With(object payload, string canonical) { _map[payload] = canonical; return this; }
        public string Serialize(object payload) => _map.TryGetValue(payload, out var s) ? s : JsonSerializer.Serialize(payload);
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(string serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }

    private sealed class CapturingSender : IInlineEventPublisher
    {
        public List<IWorkflowDefinitionVersion> ToContribute { get; init; } = new();
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is OnWorkflowVersionsReconciling rec)
                foreach (var v in ToContribute)
                    rec.Versions.Add(v);
            return Task.CompletedTask;
        }
    }

    private sealed class StubDefinitionStore : IWorkflowDefinitionStore
    {
        private readonly List<WorkflowDefinition> _items = new();
        public StubDefinitionStore With(WorkflowDefinition item) { _items.Add(item); return this; }

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.First(x => x.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_items);
    }

    private sealed class StubVersionStore : IWorkflowDefinitionVersionStore
    {
        private readonly List<WorkflowDefinitionVersion> _items = new();
        public StubVersionStore With(WorkflowDefinitionVersion item) { _items.Add(item); return this; }

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Where(x => x.DefinitionId == definitionId).OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).FirstOrDefault());

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Any(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(_items.Where(x => x.DefinitionId == definitionId).ToList());

        private const string Unused = "Not exercised by reconciler tests.";
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class SpyAddCommand<TEntity> : IAddCommand<TEntity> where TEntity : Entity
    {
        public List<TEntity> Added { get; } = new();
        public Task Add(TEntity entity, CancellationToken cancellationToken = default) { Added.Add(entity); return Task.CompletedTask; }
    }

    private sealed class SpySaveDefinitionCommand : ISaveWorkflowDefinitionCommand
    {
        public List<WorkflowDefinition> Saved { get; } = new();
        public Task Execute(WorkflowDefinition definition, CancellationToken cancellationToken = default) { Saved.Add(definition); return Task.CompletedTask; }
    }

    private sealed class SequentialIdGenerator : IIdentityGenerator
    {
        private int _i;
        public string Generate() => $"gen-{Interlocked.Increment(ref _i)}";
    }
}
