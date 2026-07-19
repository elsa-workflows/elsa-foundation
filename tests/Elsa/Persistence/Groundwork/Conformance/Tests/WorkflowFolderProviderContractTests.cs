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
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
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
        var definitions = new GroundworkWorkflowDefinitionStore(
            client.DocumentStore,
            client.BoundedDocumentStore ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
        var unfiled = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, Unfiled: true));
        Assert.Contains(unfiled.Items, definition => definition.Id == legacy.Id && definition.FolderId is null);

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
        var filed = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, FolderId: folder.Id));
        Assert.Contains(filed.Items, definition => definition.Id == "filed-definition" && definition.FolderId == folder.Id);
        var unfiledAfterFiling = await definitions.QueryPageAsync(new WorkflowDefinitionPageQuery(10, Unfiled: true));
        Assert.DoesNotContain(unfiledAfterFiling.Items, definition => definition.Id == "filed-definition");

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

    private static Task SaveLegacyDefinitionAsync(IDocumentStore store, WorkflowDefinition definition)
    {
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            definition);
        return store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

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
