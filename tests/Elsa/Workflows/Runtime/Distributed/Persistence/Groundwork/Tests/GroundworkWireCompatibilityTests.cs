using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the bridge persists the frozen v1 wire shapes unchanged. The linked fixture files are the SAME committed
/// golden fixtures the leaf's drift tests protect (single source of truth): round-tripping each fixture through the
/// bridge serializer must reproduce it exactly, and a document persisted by the bridge must embed the frozen shape
/// as its nested <c>item</c>/<c>lease</c> with only the declared index plumbing around it.
/// </summary>
public sealed class GroundworkWireCompatibilityTests
{
    private static readonly DateTimeOffset Now = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("executionCommandTransport", typeof(ExecutionCommandTransportItem))]
    [InlineData("executionPlacement", typeof(ExecutionPlacementLease))]
    public void CommittedFixture_RoundTripsThroughTheBridgeSerializer_Unchanged(string kind, Type shapeType)
    {
        var fixtureJson = ReadFixture(kind);

        var deserialized = JsonSerializer.Deserialize(fixtureJson, shapeType, DistributedGroundworkDocuments.SerializerOptions);
        Assert.NotNull(deserialized);
        var reserialized = DistributedGroundworkDocuments.Serialize(deserialized);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(fixtureJson), JsonNode.Parse(reserialized)),
            $"The bridge serializer no longer reproduces the frozen v1 '{kind}' wire shape.\n\nFixture:\n{fixtureJson}\n\nRe-serialized:\n{reserialized}");
    }

    [Fact]
    public async Task PersistedTransportDocument_EmbedsTheFrozenItemShape_PlusIndexPlumbingOnly()
    {
        var documentStore = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var harness = DistributedStoreHarness.FromDocumentStore(documentStore);

        await harness.Transport.SendAsync("wf-1", DistributedStoreHarness.Envelope("wf-1", "env-1", Now), Now);

        var envelope = Assert.Single(documentStore.Snapshot(DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind));
        Assert.Equal(DistributedGroundworkStorageManifest.SchemaVersion, envelope.SchemaVersion);

        var root = JsonNode.Parse(envelope.ContentJson)!.AsObject();
        Assert.Equal(
            new[] { "collection", "item", "workflowExecutionId" },
            root.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal(DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind, (string?)root["collection"]);
        Assert.Equal("wf-1", (string?)root["workflowExecutionId"]);

        // The nested item carries exactly the frozen shape's property names (the fixture pins values elsewhere;
        // here we pin the structural contract of what the bridge writes).
        AssertSamePropertyNames(ReadFixture("executionCommandTransport"), root["item"]!);
    }

    [Fact]
    public async Task PersistedPlacementDocument_EmbedsTheFrozenLeaseShape_PlusIndexPlumbingOnly()
    {
        var documentStore = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var harness = DistributedStoreHarness.FromDocumentStore(documentStore);

        await harness.PlacementStore.TryClaimAsync(new ExecutionPlacementClaim("wf-1", "node-a", Now, Now.AddSeconds(30)), Now);

        var envelope = Assert.Single(documentStore.Snapshot(DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind));
        Assert.Equal(DistributedGroundworkStorageManifest.SchemaVersion, envelope.SchemaVersion);

        var root = JsonNode.Parse(envelope.ContentJson)!.AsObject();
        Assert.Equal(
            new[] { "collection", "lease" },
            root.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal(DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind, (string?)root["collection"]);

        AssertSamePropertyNames(ReadFixture("executionPlacement"), root["lease"]!);
    }

    private static void AssertSamePropertyNames(string fixtureJson, JsonNode persistedNode)
    {
        var expected = JsonNode.Parse(fixtureJson)!.AsObject()
            .Select(property => property.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        var actual = persistedNode.AsObject()
            .Select(property => property.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static string ReadFixture(string kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1", kind + ".json");
        Assert.True(File.Exists(path), $"Missing linked golden fixture at '{path}'.");
        return File.ReadAllText(path);
    }
}
