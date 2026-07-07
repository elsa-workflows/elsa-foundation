using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Enums;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
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
        ISaveWorkflowDefinitionCommand? saveDef = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions { DuplicateHandling = duplicateHandling });
        return new WorkflowsVersionReconciler(
            NullLogger<WorkflowsVersionReconciler>.Instance,
            sender,
            options,
            defs,
            versions,
            addDef,
            addVer,
            saveDef ?? new SpySaveDefinitionCommand());
    }

    private static IWorkflowDefinitionVersion BuildIncomingVersion(string definitionId, string version, string name = "Stub", string? description = null) => new StubIncomingVersion
    {
        Id = string.Empty,
        Version = version,
        DefinitionId = definitionId,
        DefinitionFacade = new StubIncomingDefinition { Id = definitionId, Name = name, Description = description },
        State = new WorkflowDefinitionState([], null, [], [], null, null),
    };

    private sealed class StubIncomingDefinition : IWorkflowDefinition
    {
        public string Id { get; init; } = default!;
        public string Name { get; init; } = default!;
        public string? Description { get; init; }
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
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

        private const string Unused = "Not exercised by reconciler tests.";
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
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
