using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Persistence.Groundwork;
using Elsa.Tagging.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Tagging.Tests;

public sealed class GroundworkTagDefinitionStoreTests
{
    [Fact]
    public async Task Round_trips_active_marker_tags_and_excludes_retired_tags_from_default_list()
    {
        var store = new GroundworkTagDefinitionStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        var active = Definition("risk.pii");
        var retired = Definition("ops.legacy", TagDefinitionStatus.Retired);

        Assert.True(await store.TryAddAsync(active));
        Assert.True(await store.TryAddAsync(retired));

        Assert.Equal("risk.pii", (await store.FindByCanonicalKeyAsync("risk.pii"))!.CanonicalKey);
        Assert.Equal("risk.pii", Assert.Single(await store.ListAsync(new TagDefinitionListRequest())).CanonicalKey);
        var revisioned = await store.ListWithRevisionsAsync(new TagDefinitionListRequest { ActiveOnly = false });
        Assert.Equal(["ops.legacy", "risk.pii"], revisioned.Select(record => record.Definition.CanonicalKey));
        Assert.All(revisioned, record => Assert.False(string.IsNullOrWhiteSpace(record.Revision)));
    }

    [Fact]
    public async Task Uses_optimistic_revision_for_catalog_updates_and_create_only_append_for_audit()
    {
        var store = new GroundworkTagDefinitionStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        var definition = Definition("risk.pii");
        Assert.True(await store.TryAddAsync(definition));
        var first = (await store.FindWithRevisionAsync("risk.pii-id"))!;
        var stale = (await store.FindWithRevisionAsync("risk.pii-id"))!;

        first.Definition.DisplayName = "Personal data";
        Assert.Equal(TagDefinitionSaveStatus.Saved, (await store.SaveWithRevisionAsync(first.Definition, first.Revision)).Status);
        stale.Definition.DisplayName = "Stale";
        Assert.Equal(TagDefinitionSaveStatus.Conflict, (await store.SaveWithRevisionAsync(stale.Definition, stale.Revision)).Status);

        var audit = new TagDefinitionAuditRecord("audit-1", definition.Id, definition.CanonicalKey, "updated", DateTimeOffset.UtcNow, "author", "correlation");
        await store.AppendAsync(audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(audit).AsTask());
    }

    private static TagDefinition Definition(string canonicalKey, TagDefinitionStatus status = TagDefinitionStatus.Active) => new()
    {
        Id = canonicalKey + "-id",
        CanonicalKey = canonicalKey,
        DisplayName = canonicalKey,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
