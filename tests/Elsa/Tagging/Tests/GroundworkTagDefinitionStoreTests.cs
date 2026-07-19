using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Core.Services;
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

        var audit = Audit("audit-1", definition);
        await store.AppendAsync(audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(audit).AsTask());
    }

    [Fact]
    public async Task Does_not_persist_a_catalog_change_when_its_append_only_audit_already_exists()
    {
        var store = new GroundworkTagDefinitionStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        var first = Definition("risk.pii");
        var duplicateAudit = Audit("audit-1", first);
        await store.AppendAsync(duplicateAudit);

        var created = await store.TryAddAndAppendAuditAsync(first, duplicateAudit);

        Assert.False(created);
        Assert.Null(await store.FindByCanonicalKeyAsync(first.CanonicalKey));
    }

    [Fact]
    public async Task Lists_and_resolves_catalog_ids_beyond_one_bounded_catalog_page()
    {
        var store = new GroundworkTagDefinitionStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        for (var index = 0; index <= 250; index++)
            Assert.True(await store.TryAddAsync(Definition($"ops.tag-{index:D3}")));

        var catalog = await store.ListAsync(new TagDefinitionListRequest { ActiveOnly = false });
        var definitions = await store.ListByIdsAsync(["ops.tag-250-id"]);

        Assert.Equal(251, catalog.Count);
        Assert.Equal("ops.tag-250", Assert.Single(definitions).CanonicalKey);
    }

    [Fact]
    public async Task Controlled_values_are_unique_per_definition_and_listed_by_explicit_order()
    {
        var store = new GroundworkControlledTagValueStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        var production = Controlled("prod", "environment", "production", "Production", 20);
        var development = Controlled("dev", "environment", "development", "Development", 10);

        Assert.True(await store.TryAddAsync(production));
        Assert.True(await store.TryAddAsync(development));
        Assert.False(await store.TryAddAsync(Controlled("duplicate", "environment", "production", "Duplicate", 30)));
        Assert.True(await store.TryAddAsync(Controlled("other", "team", "production", "Production", 1)));

        var values = await store.ListWithRevisionsAsync(new ControlledTagValueListRequest { TagDefinitionId = "environment", ActiveOnly = false });
        Assert.Equal(["development", "production"], values.Select(x => x.Value.CanonicalKey));
        Assert.Equal("production", (await store.FindByCanonicalKeyAsync("environment", "production"))!.CanonicalKey);
        Assert.Equal("other", (await store.FindWithRevisionAsync("other"))!.Value.Id);
    }

    [Fact]
    public async Task Controlled_value_atomic_create_does_not_persist_when_its_audit_already_exists()
    {
        var store = new GroundworkControlledTagValueStore(new InMemoryDocumentStore(TaggingStorageManifest.Create()));
        var value = Controlled("prod", "environment", "production", "Production", 10);
        var audit = new ControlledTagValueAuditRecord("value-audit", value.Id, value.TagDefinitionId, value.CanonicalKey, "created", DateTimeOffset.UtcNow, "tenant-a", "author", "correlation", null, ControlledTagValueAuditValues.From(value));
        await store.AppendAsync(audit);

        Assert.Equal(
            ControlledTagValueCreateResult.Conflict,
            await store.TryAddWithinLimitAndAppendAuditAsync(value, audit, 0, 100));
        Assert.Null(await store.FindByCanonicalKeyAsync(value.TagDefinitionId, value.CanonicalKey));
    }

    [Fact]
    public async Task Controlled_value_limit_is_enforced_atomically_across_concurrent_creates()
    {
        var documents = new InMemoryDocumentStore(TaggingStorageManifest.Create());
        var definitions = new GroundworkTagDefinitionStore(documents);
        var values = new GroundworkControlledTagValueStore(documents);
        var definition = Definition("environment");
        definition.ValueMode = TagValueMode.Controlled;
        definition.Cardinality = TagCardinality.Single;
        Assert.True(await definitions.TryAddAsync(definition));
        for (var index = 0; index < 99; index++)
        {
            Assert.True(await values.TryAddAsync(Controlled(
                $"seed-{index:D2}",
                definition.Id,
                $"seed-{index:D2}",
                $"Seed {index:D2}",
                index)));
        }

        var manager = new DefaultControlledTagValueManager(
            definitions,
            values,
            new AuditContext(),
            TimeProvider.System,
            new CoordinatedAtomicChanges(values));
        var attempts = new[]
        {
            Task.Run(() => CreateAsync("candidate-a", "Candidate A")),
            Task.Run(() => CreateAsync("candidate-b", "Candidate B"))
        };
        var outcomes = await Task.WhenAll(attempts);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is InvalidOperationException);
        Assert.Equal(
            100,
            (await values.ListWithRevisionsAsync(new ControlledTagValueListRequest
            {
                TagDefinitionId = definition.Id,
                ActiveOnly = false
            })).Count);

        async Task<Exception?> CreateAsync(string key, string displayName)
        {
            try
            {
                await manager.CreateAsync(new CreateControlledTagValueRequest
                {
                    TagDefinitionId = definition.Id,
                    CanonicalKey = key,
                    DisplayName = displayName,
                    SortOrder = 100
                });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    private static TagDefinition Definition(string canonicalKey, TagDefinitionStatus status = TagDefinitionStatus.Active) => new()
    {
        Id = canonicalKey + "-id",
        CanonicalKey = canonicalKey,
        DisplayName = canonicalKey,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static TagDefinitionAuditRecord Audit(string id, TagDefinition definition) => new(
        id,
        definition.Id,
        definition.CanonicalKey,
        "updated",
        DateTimeOffset.UtcNow,
        "tenant-a",
        "author",
        "correlation",
        new TagDefinitionAuditValues("Before", null, null, TagDefinitionStatus.Active),
        new TagDefinitionAuditValues(definition.DisplayName, definition.Description, definition.Color, definition.Status));

    private static ControlledTagValue Controlled(string id, string definitionId, string canonicalKey, string displayName, int sortOrder) => new()
    {
        Id = id,
        TagDefinitionId = definitionId,
        CanonicalKey = canonicalKey,
        DisplayName = displayName,
        SortOrder = sortOrder,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed class AuditContext : ITagDefinitionAuditContext
    {
        public string Actor => "author";
        public string CorrelationId => "correlation";
        public string? TenantId => "tenant-a";
    }

    private sealed class CoordinatedAtomicChanges(
        IControlledTagValueAtomicChangeStore inner) : IControlledTagValueAtomicChangeStore
    {
        private readonly Barrier barrier = new(2);
        private int calls;

        public async ValueTask<ControlledTagValueCreateResult> TryAddWithinLimitAndAppendAuditAsync(
            ControlledTagValue value,
            ControlledTagValueAuditRecord audit,
            int expectedCount,
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) <= 2)
                barrier.SignalAndWait(cancellationToken);
            return await inner.TryAddWithinLimitAndAppendAuditAsync(
                value,
                audit,
                expectedCount,
                maximumCount,
                cancellationToken);
        }

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(
            ControlledTagValue value,
            string expectedRevision,
            ControlledTagValueAuditRecord audit,
            CancellationToken cancellationToken = default) =>
            inner.SaveWithRevisionAndAppendAuditAsync(
                value,
                expectedRevision,
                audit,
                cancellationToken);
    }
}
