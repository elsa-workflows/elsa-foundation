using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task StoresEnforceCasAndSurviveAdapterRestart(string provider)
    {
        await using var fixture = await PublishingStoreFixture.CreateAsync(provider);
        var serializer = new PublishingGroundworkDocumentSerializer();
        var stores = Stores.Create(fixture.Store, serializer);

        var initial = await stores.Slots.TryActivateAsync("definition-1", "default", "publication-current", 0, Now);
        Assert.True(initial.Succeeded);
        var duplicateAuthority = await stores.Slots.TryActivateAsync("definition-1", "blue", "publication-current", 0, Now);
        Assert.False(duplicateAuthority.Succeeded);
        Assert.Equal("publication_already_active", duplicateAuthority.Failure?.Code);
        var concurrent = await Task.WhenAll(
            stores.Slots.TryActivateAsync("definition-1", "default", "publication-a", 1, Now.AddMinutes(1)).AsTask(),
            stores.Slots.TryActivateAsync("definition-1", "default", "publication-b", 1, Now.AddMinutes(1)).AsTask());
        Assert.Single(concurrent, x => x.Succeeded);
        Assert.Single(concurrent, x => !x.Succeeded && x.Failure?.Code == "slot_revision_conflict");

        var candidate = Publication("publication-record", PublicationStatus.Candidate);
        await stores.Publications.SaveAsync(candidate);
        var transitions = await Task.WhenAll(
            stores.Publications.TryTransitionAsync(candidate with { Status = PublicationStatus.Active, ActivatedAt = Now }, PublicationStatus.Candidate).AsTask(),
            stores.Publications.TryTransitionAsync(candidate with { Status = PublicationStatus.Failed, Failure = new PublicationFailure("lost", "Lost CAS") }, PublicationStatus.Candidate).AsTask());
        Assert.Single(transitions, x => x);

        var policy = new PublicationPolicy("definition-1", PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, Now);
        var policyWrites = await Task.WhenAll(
            stores.Policies.TrySaveAsync(policy, 0).AsTask(),
            stores.Policies.TrySaveAsync(policy with { DefaultSlotName = "blue" }, 0).AsTask());
        Assert.Single(policyWrites, x => x.Succeeded);

        var intent = new PublicationProjectionIntent(
            "intent-1", "publication-record", PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Prepare, PublicationProjectionIntentStatus.Pending, 0, null, null);
        await stores.Intents.SaveAsync(intent);
        var claimed = await stores.Intents.TryTransitionAsync(
            intent with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 1 },
            PublicationProjectionIntentStatus.Pending);
        Assert.True(claimed.Succeeded);

        await fixture.RestartAsync();
        stores = Stores.Create(fixture.Store, serializer);

        var slot = await stores.Slots.FindAsync("definition-1", "default");
        Assert.Equal(2, slot!.Revision);
        Assert.Contains(slot.ActivePublicationId, new[] { "publication-a", "publication-b" });
        Assert.Single(await stores.Slots.ListByDefinitionAsync("definition-1"));
        Assert.Single(await stores.Publications.ListBySlotAsync(candidate.SlotId));
        Assert.NotEqual(PublicationStatus.Candidate, (await stores.Publications.FindAsync(candidate.PublicationId))!.Status);
        Assert.Equal(1, (await stores.Policies.FindAsync("definition-1"))!.Revision);
        Assert.Equal(PublicationProjectionIntentStatus.Delivering, (await stores.Intents.FindAsync("intent-1"))!.Status);
        Assert.Single(await stores.Intents.ListByPublicationAsync("publication-record"));
    }

    private static PublicationRecord Publication(string id, PublicationStatus status) => new(
        id,
        PublicationSlotIdentity.Create("definition-1", "default"),
        "definition-1",
        "version-1",
        "artifact-1",
        "reference-1",
        0,
        status,
        Now,
        null,
        null,
        null);

    private sealed record Stores(
        GroundworkPublicationSlotStore Slots,
        GroundworkPublicationRecordStore Publications,
        GroundworkPublicationPolicyStore Policies,
        GroundworkPublicationProjectionIntentStore Intents)
    {
        public static Stores Create(IDocumentStore store, PublishingGroundworkDocumentSerializer serializer) => new(
            new GroundworkPublicationSlotStore(store, serializer),
            new GroundworkPublicationRecordStore(store, serializer),
            new GroundworkPublicationPolicyStore(store, serializer),
            new GroundworkPublicationProjectionIntentStore(store, serializer));
    }

    private sealed class PublishingStoreFixture(
        string provider,
        string? sqlitePath,
        IDocumentStore store) : IAsyncDisposable
    {
        private static readonly ProviderIdentity SqliteProvider = new("publishing-groundwork-sqlite-tests", "1.0.0");
        public IDocumentStore Store { get; private set; } = store;

        public static async Task<PublishingStoreFixture> CreateAsync(string provider)
        {
            if (provider == "memory")
                return new PublishingStoreFixture(provider, null, new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create()));
            var path = Path.Combine(Path.GetTempPath(), $"elsa-publishing-{Guid.NewGuid():N}.db");
            var handle = await SqliteDocumentStoreFactory.CreateAsync(
                $"Data Source={path}", PublishingGroundworkStorageManifest.Create(), SqliteProvider, DocumentStoreAccess.Global);
            return new PublishingStoreFixture(provider, path, handle);
        }

        public async Task RestartAsync()
        {
            if (provider == "memory")
                return;
            Store = await SqliteDocumentStoreFactory.CreateAsync(
                $"Data Source={sqlitePath}", PublishingGroundworkStorageManifest.Create(), SqliteProvider, DocumentStoreAccess.Global);
        }

        public ValueTask DisposeAsync()
        {
            if (sqlitePath is not null && File.Exists(sqlitePath))
                File.Delete(sqlitePath);
            return ValueTask.CompletedTask;
        }
    }
}
