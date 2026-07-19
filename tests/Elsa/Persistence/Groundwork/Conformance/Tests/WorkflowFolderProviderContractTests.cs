using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.Scoping;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Proves the folder invariants against each actual physical provider. These scenarios deliberately
/// use independently opened clients, because the unique sibling constraint is a provider boundary,
/// not an in-memory-store behavior.
/// </summary>
public sealed class WorkflowFolderProviderContractTests
{
    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Direct_children_are_tenant_scoped_ordered_and_resumable_after_reopen_on_every_provider(string providerKey)
    {
        await using var driver = await InitializeAsync(providerKey);
        const string tenantA = "tenant-a";
        await using (var client = await driver.OpenPhysicalClientAsync(Access(tenantA)))
        {
            var folders = Store(client, tenantA);
            await folders.CreateAsync(Folder("root-b", "Beta"));
            await folders.CreateAsync(Folder("root-a", "alpha"));
            await folders.CreateAsync(Folder("nested-a", "Alpha child", "root-a"));

            var first = await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 1));
            Assert.Equal("root-a", Assert.Single(first.Items).Id);
            Assert.NotNull(first.NextContinuationToken);
        }

        string continuation;
        await using (var firstReader = await driver.OpenPhysicalClientAsync(Access(tenantA)))
        {
            var folders = Store(firstReader, tenantA);
            var first = await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 1));
            continuation = Assert.IsType<string>(first.NextContinuationToken);
            var nested = await folders.FindWithAncestorsAsync("nested-a");
            Assert.NotNull(nested);
            Assert.Equal("nested-a", nested!.Folder.Id);
            Assert.Equal(["root-a"], nested.Ancestors.Select(folder => folder.Id));
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync(Access(tenantA)))
        {
            var folders = Store(reopened, tenantA);
            var second = await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 1, continuation));
            Assert.Equal("root-b", Assert.Single(second.Items).Id);
            Assert.Null(second.NextContinuationToken);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest("root-a", 1, continuation)));
        }

        await using var tenantBClient = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        var tenantBFolders = Store(tenantBClient, "tenant-b");
        await tenantBFolders.CreateAsync(Folder("root-a", "Alpha"));
        Assert.Equal("root-a", Assert.Single((await tenantBFolders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 10))).Items).Id);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            tenantBFolders.CreateAsync(Folder("cross-tenant-child", "No access", "nested-a")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Concurrent_root_and_nested_sibling_creates_commit_exactly_one_folder_without_orphans_on_every_provider(string providerKey)
    {
        await using var driver = await InitializeAsync(providerKey);
        const string tenant = "tenant-a";
        await using (var seedClient = await driver.OpenPhysicalClientAsync(Access(tenant)))
            await Store(seedClient, tenant).CreateAsync(Folder("parent", "Parent"));

        await using var first = await driver.OpenPhysicalClientAsync(Access(tenant));
        await using var second = await driver.OpenPhysicalClientAsync(Access(tenant));
        var firstFolders = Store(first, tenant);
        var secondFolders = Store(second, tenant);

        var rootRace = await Task.WhenAll(
            CreateOutcomeAsync(firstFolders, Folder("root-first", "Shared")),
            CreateOutcomeAsync(secondFolders, Folder("root-second", "shared")));
        Assert.Single(rootRace, outcome => outcome is null);
        Assert.Single(rootRace, outcome => outcome is WorkflowFolderSiblingConflictException);

        var nestedRace = await Task.WhenAll(
            CreateOutcomeAsync(firstFolders, Folder("nested-first", "Shared", "parent")),
            CreateOutcomeAsync(secondFolders, Folder("nested-second", "shared", "parent")));
        Assert.Single(nestedRace, outcome => outcome is null);
        Assert.Single(nestedRace, outcome => outcome is WorkflowFolderSiblingConflictException);

        await using var verifier = await driver.OpenPhysicalClientAsync(Access(tenant));
        var folders = Store(verifier, tenant);
        Assert.Single((await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 10))).Items, folder => folder.NormalizedName == "SHARED");
        Assert.Single((await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest("parent", 10))).Items, folder => folder.NormalizedName == "SHARED");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Parent_depth_and_legacy_unfiled_definition_contracts_hold_on_every_provider(string providerKey)
    {
        await using var driver = await InitializeAsync(providerKey);
        const string tenant = "tenant-a";
        await using var client = await driver.OpenPhysicalClientAsync(Access(tenant));
        var folders = Store(client, tenant);
        string? parentId = null;
        for (var depth = 0; depth < WorkflowFolderNames.MaximumDepth; depth++)
        {
            var created = await folders.CreateAsync(Folder($"depth-{depth}", $"Depth {depth}", parentId));
            parentId = created.Id;
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            folders.CreateAsync(Folder("too-deep", "Too deep", parentId)));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            folders.CreateAsync(Folder("missing-parent", "Missing", "does-not-exist")));

        var legacy = new WorkflowDefinition
        {
            Id = "legacy-unfiled",
            Name = "Legacy unfiled",
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        await SaveLegacyDefinitionAsync(client.DocumentStore, legacy);
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
        var definitions = new GroundworkWorkflowDefinitionStore(client.DocumentStore, bounded);
        var unfiled = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, Unfiled: true));
        Assert.Contains(unfiled.Items, definition => definition.Id == legacy.Id && definition.FolderId is null);
        Assert.Equal(
            WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
            Assert.Single(bounded.Observations).Query.QueryIdentity);

        var folder = await folders.CreateAsync(Folder("definition-folder", "Definition folder"));
        var addDefinition = new GroundworkAddWorkflowDefinitionCommand(
            client.DocumentStore,
            new PayloadSerializer(),
            new Clock(),
            GroundworkTestAccess.AccessContext(tenant));
        await addDefinition.Execute(
            new WorkflowDefinition { Id = "filed-definition", Name = "Filed", TenantId = tenant, FolderId = folder.Id },
            new WorkflowDefinitionDraft
            {
                Id = "filed-draft",
                WorkflowDefinitionId = "filed-definition",
                TenantId = tenant,
                State = new WorkflowDefinitionState([], null, [], [], null)
            },
            [],
            CancellationToken.None);
        var legacyDeleted = new WorkflowDefinition
        {
            Id = "legacy-deleted-filed",
            Name = "Legacy deleted filed",
            TenantId = tenant,
            FolderId = folder.Id,
            DeletedAt = DateTimeOffset.UnixEpoch,
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        await SaveLegacyDefinitionAsync(client.DocumentStore, legacyDeleted);

        var filed = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, FolderId: folder.Id));
        Assert.Contains(filed.Items, definition => definition.Id == "filed-definition" && definition.FolderId == folder.Id);
        Assert.DoesNotContain(filed.Items, definition => definition.Id == legacyDeleted.Id);
        var unfiledAfterFiling = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, Unfiled: true));
        Assert.DoesNotContain(unfiledAfterFiling.Items, definition => definition.Id == "filed-definition");
        var deletedFiled = await definitions.QueryPageAsync(
            new WorkflowDefinitionPageQuery(10, State: WorkflowDefinitionPageState.Deleted, FolderId: folder.Id));
        Assert.Contains(deletedFiled.Items, definition => definition.Id == legacyDeleted.Id);
        Assert.DoesNotContain(deletedFiled.Items, definition => definition.Id == "filed-definition");
        var allFiled = await definitions.QueryPageAsync(
            new WorkflowDefinitionPageQuery(10, State: WorkflowDefinitionPageState.All, FolderId: folder.Id));
        Assert.Contains(allFiled.Items, definition => definition.Id == "filed-definition");
        Assert.Contains(allFiled.Items, definition => definition.Id == legacyDeleted.Id);
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
                WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
                WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
                WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
                WorkflowsDesignStorageManifest.PageAllWorkflowDefinitionsByFolderQuery
            ],
            bounded.Observations.Select(observation => observation.Query.QueryIdentity));
        Assert.All(bounded.Observations, observation =>
        {
            Assert.Equal(10, observation.Query.Take);
            Assert.InRange(observation.MaterializedDocuments, 0, 10);
        });
        var explainer = client.PhysicalDocumentQueryExplainer
            ?? throw new InvalidOperationException("The provider did not expose native query explanations.");
        foreach (var observation in bounded.Observations.DistinctBy(item => item.Query.QueryIdentity))
        {
            var explanation = await explainer.ExplainAsync(observation.Query, CancellationToken.None);
            Assert.Equal(observation.Query.QueryIdentity, explanation.Plan.QueryIdentity);
            Assert.NotEmpty(explanation.Commands);
        }

        await using var tenantBClient = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        var crossTenantAdd = new GroundworkAddWorkflowDefinitionCommand(
            tenantBClient.DocumentStore,
            new PayloadSerializer(),
            new Clock(),
            GroundworkTestAccess.AccessContext("tenant-b"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => crossTenantAdd.Execute(
            new WorkflowDefinition { Id = "cross-tenant-definition", Name = "Cross", TenantId = "tenant-b", FolderId = folder.Id },
            new WorkflowDefinitionDraft
            {
                Id = "cross-tenant-draft",
                WorkflowDefinitionId = "cross-tenant-definition",
                TenantId = "tenant-b",
                State = new WorkflowDefinitionState([], null, [], [], null)
            },
            [],
            CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Definition_folder_moves_are_atomic_tenant_scoped_and_preserve_lifecycle_placement_on_every_provider(string providerKey)
    {
        await using var driver = await InitializeAsync(providerKey);
        const string tenantA = "tenant-a";
        await using var client = await driver.OpenPhysicalClientAsync(Access(tenantA));
        var folders = Store(client, tenantA);
        var destination = await folders.CreateAsync(Folder("destination", "Destination"));
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext(tenantA);
        var save = new GroundworkSaveWorkflowDefinitionCommand(client.DocumentStore, clock, access);
        await save.Execute(new WorkflowDefinition { Id = "one", Name = "One", TenantId = tenantA, FolderId = "before" });
        await save.Execute(new WorkflowDefinition { Id = "two", Name = "Two", TenantId = tenantA, FolderId = "before", DeletedAt = DateTimeOffset.UnixEpoch });

        var move = new GroundworkMoveWorkflowDefinitionsCommand(client.DocumentStore, clock, access);
        await move.Execute(["one", "two"], destination.Id);

        var definitions = new GroundworkWorkflowDefinitionStore(client.DocumentStore);
        Assert.Equal(destination.Id, (await definitions.GetAsync("one")).FolderId);
        var deleted = await definitions.GetAsync("two");
        Assert.Equal(destination.Id, deleted.FolderId);
        Assert.Equal(DateTimeOffset.UnixEpoch, deleted.DeletedAt);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => move.Execute(["one", "missing"], null));
        Assert.Equal(destination.Id, (await definitions.GetAsync("one")).FolderId);

        await using var foreign = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new GroundworkMoveWorkflowDefinitionsCommand(foreign.DocumentStore, clock, GroundworkTestAccess.AccessContext("tenant-b"))
                .Execute(["one"], null));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Concurrent_two_definition_moves_select_one_atomic_winner_on_transaction_capable_providers(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        var required =
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions;
        Skip.IfNot(
            (driver.Descriptor.Topology.Capabilities & required) == required,
            $"{providerKey} does not advertise independent clients plus multi-document transactions.");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);

        const string tenant = "tenant-a";
        await using (var seedClient = await driver.OpenPhysicalClientAsync(Access(tenant)))
        {
            var access = GroundworkTestAccess.AccessContext(tenant);
            var clock = new Clock();
            var folders = Store(seedClient, tenant);
            await folders.CreateAsync(Folder("destination-a", "Destination A"));
            await folders.CreateAsync(Folder("destination-b", "Destination B"));
            var save = new GroundworkSaveWorkflowDefinitionCommand(seedClient.DocumentStore, clock, access);
            await save.Execute(new WorkflowDefinition
            {
                Id = "one",
                Name = "One",
                Description = "Preserve one",
                TenantId = tenant,
                FolderId = "before",
                DeletedReason = "keep-reason"
            });
            await save.Execute(new WorkflowDefinition
            {
                Id = "two",
                Name = "Two",
                Description = "Preserve two",
                TenantId = tenant,
                FolderId = "before",
                DeletedAt = DateTimeOffset.UnixEpoch
            });
        }

        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(tenant));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(tenant));
        Assert.Equal(TransactionBoundary.CrossUnitAtomic, firstClient.DocumentStore.TransactionBoundary);
        Assert.Equal(TransactionBoundary.CrossUnitAtomic, secondClient.DocumentStore.TransactionBoundary);
        var barrier = new SharedUnitOfWorkLoadBarrier(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            "one");
        var accessContext = GroundworkTestAccess.AccessContext(tenant);
        var firstMove = new GroundworkMoveWorkflowDefinitionsCommand(
            new UnitOfWorkLoadBarrierDocumentStore(firstClient.DocumentStore, barrier),
            new Clock(),
            accessContext);
        var secondMove = new GroundworkMoveWorkflowDefinitionsCommand(
            new UnitOfWorkLoadBarrierDocumentStore(secondClient.DocumentStore, barrier),
            new Clock(),
            accessContext);

        var outcomes = await Task.WhenAll(
            MoveOutcomeAsync(firstMove, "destination-a"),
            MoveOutcomeAsync(secondMove, "destination-b"));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is WorkflowDefinitionFolderMoveConflictException);

        await using var verifierClient = await driver.OpenPhysicalClientAsync(Access(tenant));
        var verifier = new GroundworkWorkflowDefinitionStore(verifierClient.DocumentStore);
        var first = await verifier.GetAsync("one");
        var second = await verifier.GetAsync("two");
        Assert.Equal(first.FolderId, second.FolderId);
        Assert.Contains(first.FolderId, new[] { "destination-a", "destination-b" });
        Assert.Equal("One", first.Name);
        Assert.Equal("Preserve one", first.Description);
        Assert.Equal("keep-reason", first.DeletedReason);
        Assert.Equal("Two", second.Name);
        Assert.Equal("Preserve two", second.Description);
        Assert.Equal(DateTimeOffset.UnixEpoch, second.DeletedAt);
    }

    private static async Task<GroundworkProviderDriver> InitializeAsync(string providerKey)
    {
        var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);
        return driver;
    }

    private static GroundworkWorkflowFolderStore Store(GroundworkProviderClient client, string tenant) =>
        new(
            client.DocumentStore,
            GroundworkTestAccess.AccessContext(tenant),
            new Clock(),
            client.BoundedDocumentStore ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));

    private static DocumentStoreAccess Access(string tenant) =>
        DocumentStoreAccess.Scoped(new StorageScope(tenant));

    private static WorkflowFolder Folder(string id, string name, string? parentId = null)
    {
        var normalized = WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder
        {
            Id = id,
            ParentFolderId = parentId,
            Name = normalized.Name,
            NormalizedName = normalized.NormalizedName
        };
    }

    private static async Task<Exception?> CreateOutcomeAsync(GroundworkWorkflowFolderStore store, WorkflowFolder folder)
    {
        try
        {
            await store.CreateAsync(folder);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> MoveOutcomeAsync(
        GroundworkMoveWorkflowDefinitionsCommand command,
        string destination)
    {
        try
        {
            await command.Execute(["one", "two"], destination);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Task SaveLegacyDefinitionAsync(IDocumentStore store, WorkflowDefinition definition)
    {
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            definition);
        var canonicalJson = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("lifecycleKey", canonicalJson, StringComparison.OrdinalIgnoreCase);
        return store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            canonicalJson));
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class SharedUnitOfWorkLoadBarrier(string documentKind, string id)
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task WaitAsync(
            string observedDocumentKind,
            string observedId,
            CancellationToken cancellationToken)
        {
            if (!StringComparer.Ordinal.Equals(documentKind, observedDocumentKind) ||
                !StringComparer.Ordinal.Equals(id, observedId))
                return;

            if (Interlocked.Increment(ref _arrivals) == 2)
                _allArrived.TrySetResult();

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class UnitOfWorkLoadBarrierDocumentStore(
        IDocumentStore inner,
        SharedUnitOfWorkLoadBarrier barrier) : IDocumentStore
    {
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IDocumentUnitOfWork>(
                new LoadBarrierDocumentUnitOfWork(inner, scope, barrier));
    }

    private sealed class LoadBarrierDocumentUnitOfWork(
        IDocumentStore store,
        DocumentCommitScope scope,
        SharedUnitOfWorkLoadBarrier barrier) : IDocumentUnitOfWork
    {
        private IDocumentUnitOfWork? _inner;

        public async Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            await (await InnerAsync(cancellationToken)).SaveAsync(request, cancellationToken);

        public async Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            await (await InnerAsync(cancellationToken)).DeleteAsync(request, cancellationToken);

        public async Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            // Delay the physical transaction until the first write. This lets both independent
            // clients observe the same native provider versions before either writer acquires a
            // provider transaction, while all writes still commit through a real atomic UoW.
            var envelope = await store.LoadAsync(documentKind, id, cancellationToken);
            await barrier.WaitAsync(documentKind, id, cancellationToken);
            return envelope;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default) =>
            await (await InnerAsync(cancellationToken)).CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            _inner?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

        public ValueTask DisposeAsync() => _inner?.DisposeAsync() ?? ValueTask.CompletedTask;

        private async Task<IDocumentUnitOfWork> InnerAsync(CancellationToken cancellationToken) =>
            _inner ??= await store.BeginAsync(scope, cancellationToken);
    }

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        public List<QueryObservation> Observations { get; } = [];

        public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            var result = await inner.QueryAsync(query, cancellationToken);
            Observations.Add(new QueryObservation(query, result.Documents.Count));
            return result;
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
    }

    private sealed record QueryObservation(DocumentQuery Query, int MaterializedDocuments);

    private sealed class PayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }
}
