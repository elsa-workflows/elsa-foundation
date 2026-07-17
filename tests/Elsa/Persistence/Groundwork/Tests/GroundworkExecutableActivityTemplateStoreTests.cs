using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkExecutableActivityTemplateStoreTests
{
    [Fact]
    public async Task RoundTrips_By_Id_And_Hash_With_Stable_Descriptor_Shape()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);
        var template = Template("template-1", "sha256:one", "node-1");

        await store.SaveAsync(template);

        var byId = await store.FindAsync("template-1");
        var byHash = await store.FindByHashAsync("sha256:one");
        Assert.NotNull(byId);
        Assert.Equal("template-1", byId!.TemplateId);
        Assert.Equal("node-1", byId.Root.ExecutableNodeId);
        Assert.True(byId.NodesById.ContainsKey("node-1"));
        Assert.Equal(new RuntimeRequirement("test.consumer", "1"), Assert.Single(byId.RuntimeRequirements));
        Assert.Equal("sample.external", Assert.Single(byId.StorageDriverRequirements).DriverKey);
        Assert.Equal("template-1", byHash!.TemplateId);

        var envelope = Assert.Single(documents.Snapshot(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind));
        Assert.Equal("2", envelope.SchemaVersion);
        using var content = JsonDocument.Parse(envelope.ContentJson);
        var root = content.RootElement;
        Assert.Equal(ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection, root.GetProperty("collection").GetString());
        Assert.Equal("sha256:one", root.GetProperty("templateHash").GetString());
        var persistedTemplate = root.GetProperty("template");
        Assert.False(persistedTemplate.TryGetProperty("nodesById", out _));
        var persistedRoot = persistedTemplate.GetProperty("root");
        Assert.Equal("test.consumer", persistedRoot.GetProperty("descriptorType").GetString());
        Assert.Equal("1", persistedRoot.GetProperty("descriptorSchemaVersion").GetString());
        Assert.Equal("node-1", persistedRoot.GetProperty("descriptorPayload").GetProperty("id").GetString());
        Assert.False(persistedRoot.TryGetProperty("descriptor", out _));
    }

    [Fact]
    public async Task Exact_Resave_Is_Idempotent()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Template(
            "template-1",
            "sha256:one",
            "node-1",
            DateTimeOffset.UnixEpoch.AddMinutes(1)));
        await store.SaveAsync(Template("template-1", "sha256:one", "node-1"));

        var envelope = Assert.Single(documents.Snapshot(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind));
        Assert.Equal(1, envelope.Version);
    }

    [Fact]
    public async Task Rejects_Id_Hash_And_Content_Collisions_Without_Overwriting()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);
        await store.SaveAsync(Template("template-1", "sha256:one", "node-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-1", "sha256:other", "node-2")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-other", "sha256:one", "node-3")));
        var contentCollision = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-1", "sha256:one", "different-node")));

        Assert.Contains("different content", contentCollision.Message, StringComparison.Ordinal);
        Assert.Equal("node-1", (await store.FindAsync("template-1"))!.Root.ExecutableNodeId);
        Assert.Single(documents.Snapshot(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind));
    }

    [Fact]
    public async Task SaveAsync_RejectsConcurrentHashWinnerWithDifferentId()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var competingStore = new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(documents)
        {
            OnBeforeBegin = async _ =>
            {
                await competingStore.SaveAsync(Template("template-1", "sha256:one", "node-1"));
            }
        };
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(interceptingStore, GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-2", "sha256:one", "node-2")));

        Assert.Contains("already bound to id 'template-1'", exception.Message, StringComparison.Ordinal);
        Assert.Equal("template-1", (await competingStore.FindByHashAsync("sha256:one"))!.TemplateId);
        Assert.Null(await competingStore.FindAsync("template-2"));
    }

    [Fact]
    public async Task Delete_Removes_Template_And_Hash_Claim()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Template("template-1", "sha256:one", "node-1"));
        Assert.True(await store.DeleteAsync("template-1"));
        await store.SaveAsync(Template("template-2", "sha256:one", "node-2"));

        Assert.Null(await store.FindAsync("template-1"));
        Assert.Equal("template-2", (await store.FindByHashAsync("sha256:one"))!.TemplateId);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotRemoveConcurrentSuccessorHashClaim()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var competingStore = new GroundworkExecutableActivityTemplateStore(documents, GroundworkTestSerialization.Serializer);
        await competingStore.SaveAsync(Template("template-1", "sha256:one", "node-1"));
        var interceptingStore = new InterceptingDocumentStore(documents)
        {
            OnBeforeBegin = async _ =>
            {
                Assert.True(await competingStore.DeleteAsync("template-1"));
                await competingStore.SaveAsync(Template("template-2", "sha256:one", "node-2"));
            }
        };
        IExecutableActivityTemplateStore store =
            new GroundworkExecutableActivityTemplateStore(interceptingStore, GroundworkTestSerialization.Serializer);

        Assert.False(await store.DeleteAsync("template-1"));

        Assert.Equal("template-2", (await competingStore.FindByHashAsync("sha256:one"))!.TemplateId);
        Assert.NotNull(await competingStore.FindAsync("template-2"));
    }

    private static ExecutableActivityTemplate Template(
        string id,
        string hash,
        string nodeId,
        DateTimeOffset? createdAt = null) => new(
        id,
        hash,
        Node(nodeId),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [],
        [],
        [new RuntimeRequirement("test.consumer", "1")],
        "provider/1",
        new Dictionary<string, string> { ["wire"] = "stable-descriptor" },
        createdAt ?? DateTimeOffset.UnixEpoch,
        [new RuntimeStorageDriverRequirement("sample.external")]);

    private static ExecutableNode Node(string id) => new(
        id,
        $"authored-{id}",
        "test.activity",
        "1",
        new RuntimeActivityDescriptor(
            "test.consumer",
            "1",
            JsonSerializer.SerializeToElement(new { id })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());
}
