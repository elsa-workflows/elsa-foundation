using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkFixtureTests
{
    private readonly InMemoryDocumentStore _documents = new(PublishingGroundworkStorageManifest.Create());
    private readonly PublishingGroundworkDocumentSerializer _serializer = new();

    [Fact]
    public async Task FrozenV1FixturesDeserializeThroughEveryStore()
    {
        var slotId = PublicationSlotIdentity.Create("definition-1", "default");
        await SeedAsync(PublishingGroundworkStorageManifest.PublicationSlotDocumentKind, slotId, "publicationSlot.json");
        await SeedAsync(PublishingGroundworkStorageManifest.PublicationRecordDocumentKind, "publication-1", "publicationRecord.json");
        await SeedAsync(PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind, "workflow:12:definition-1", "publicationPolicy.json");
        await SeedAsync(PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind, "intent-1", "projectionIntent.json");

        var slot = await new GroundworkPublicationSlotStore(_documents, _serializer).FindAsync("definition-1", "default");
        var publication = await new GroundworkPublicationRecordStore(_documents, _serializer).FindAsync("publication-1");
        var policy = await new GroundworkPublicationPolicyStore(_documents, _serializer).FindAsync("definition-1");
        var intent = await new GroundworkPublicationProjectionIntentStore(_documents, _serializer).FindAsync("intent-1");

        Assert.Equal("publication-1", slot!.ActivePublicationId);
        Assert.Equal(PublicationStatus.Active, publication!.Status);
        Assert.Equal(1, policy!.Revision);
        Assert.Equal(PublicationProjectionIntentStatus.Pending, intent!.Status);
    }

    [Fact]
    public void EveryPublishingDocumentKindStartsAtV1AndRejectsUnknownKinds()
    {
        var kinds = new[]
        {
            PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
            PublishingGroundworkStorageManifest.PublicationRecordDocumentKind,
            PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind,
            PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind
        };
        Assert.All(kinds, kind => Assert.Equal(1, PublishingGroundworkDocumentSerializer.CurrentFor(kind)));
        Assert.Throws<ArgumentException>(() => PublishingGroundworkDocumentSerializer.CurrentFor("unknown"));
    }

    [Fact]
    public void SerializerRejectsMissingUpcastStepsAndFutureVersions()
    {
        var content = "{\"workflowDefinitionId\":\"definition-1\",\"slot\":{}}";
        var old = Envelope("0.0.0", content);
        var future = Envelope("2.0.0", content);

        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize<object>(old));
        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize<object>(future));
    }

    private async Task SeedAsync(string kind, string id, string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1", fixtureName);
        await _documents.SaveAsync(new SaveDocumentRequest(kind, id, "1.0.0", await File.ReadAllTextAsync(path)));
    }

    private static DocumentEnvelope Envelope(string schemaVersion, string content) => new(
        PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
        "slot-1",
        schemaVersion,
        1,
        content,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
}
