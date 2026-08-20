using System.Text.Json;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkFixtureTests
{
    private readonly PublishingGroundworkDocumentSerializer serializer = new();

    [Fact]
    public async Task CurrentFixturesDeserializeThroughEveryStore()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var access = persistence.Access();
        var slotStore = new GroundworkPublicationSlotStore(persistence.Sessions, access, serializer);
        var publicationStore = new GroundworkPublicationRecordStore(persistence.Sessions, access, serializer);
        var policyStore = new GroundworkPublicationPolicyStore(persistence.Sessions, access, serializer);
        var intentStore = new GroundworkPublicationProjectionIntentStore(persistence.Sessions, access, serializer);

        var slot = Read<SlotFixture>("publicationSlot.json").Slot;
        var publication = Read<PublicationFixture>("publicationRecord.json").Publication;
        var policy = Read<PolicyFixture>("publicationPolicy.json").Policy;
        var intent = Read<IntentFixture>("projectionIntent.json").Intent;
        await slotStore.TryActivateAsync(slot.WorkflowDefinitionId, slot.SlotName, slot.ActivePublicationId!, 0, slot.UpdatedAt);
        await publicationStore.SaveAsync(publication);
        await policyStore.TrySaveAsync(policy, 0);
        await intentStore.SaveAsync(intent);

        Assert.Equal("publication-1", (await slotStore.FindAsync("definition-1", "default"))!.ActivePublicationId);
        Assert.Equal(PublicationStatus.Active, (await publicationStore.FindAsync("publication-1"))!.Status);
        Assert.Equal(1, (await policyStore.FindAsync("definition-1"))!.Revision);
        Assert.Equal(PublicationProjectionIntentStatus.Pending, (await intentStore.FindAsync("intent-1"))!.Status);
    }

    [Fact]
    public void EveryPublishingDocumentKindUsesTheCurrentSchemaAndRejectsUnknownKinds()
    {
        var kinds = new[]
        {
            PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
            PublishingGroundworkStorageManifest.PublicationRecordDocumentKind,
            PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind,
            PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind,
            PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind,
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind
        };
        Assert.All(kinds, kind => Assert.Equal(PublishingGroundworkStorageManifest.SchemaVersion,
            serializer.Serialize(kind, new { value = "current" }).SchemaVersion));
        Assert.Throws<ArgumentException>(() => serializer.Serialize("unknown", new { value = "current" }));
    }

    [Fact]
    public void SerializerRejectsMalformedContentWithoutCompatibilityPaths()
    {
        Assert.Throws<ArgumentException>(() => serializer.Deserialize<object>(
            "unknown", "id", PublishingGroundworkStorageManifest.SchemaVersion, "{}"));
        Assert.ThrowsAny<JsonException>(() => serializer.Deserialize<object>(
            PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
            "id",
            PublishingGroundworkStorageManifest.SchemaVersion,
            "not-json"));
    }

    private T Read<T>(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "current", fixtureName);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidDataException($"Fixture '{fixtureName}' is empty.");
    }

    private sealed record SlotFixture(string WorkflowDefinitionId, PublicationSlot Slot);
    private sealed record PublicationFixture(string SlotId, PublicationRecord Publication);
    private sealed record PolicyFixture(string WorkflowDefinitionId, PublicationPolicy Policy);
    private sealed record IntentFixture(string PublicationId, PublicationProjectionIntent Intent);
}
