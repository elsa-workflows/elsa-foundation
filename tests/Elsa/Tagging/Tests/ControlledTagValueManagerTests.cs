using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Core.Services;
using Xunit;

namespace Elsa.Tagging.Tests;

public sealed class ControlledTagValueManagerTests
{
    [Fact]
    public async Task Creates_a_stable_controlled_value_and_records_its_presentation_audit()
    {
        var definitions = new Definitions(ControlledDefinition());
        var audit = new Audits();
        var values = new Values(audit);
        var manager = new DefaultControlledTagValueManager(definitions, values, new Context(), TimeProvider.System, values);

        var value = await manager.CreateAsync(new CreateControlledTagValueRequest
        {
            TagDefinitionId = "environment",
            CanonicalKey = "production",
            DisplayName = "Production",
            SortOrder = 20,
            Color = "#00AA00"
        });

        Assert.NotEmpty(value.Id);
        Assert.Equal("environment", value.TagDefinitionId);
        Assert.Equal("production", value.CanonicalKey);
        var record = Assert.Single(audit.Records);
        Assert.Equal(value.Id, record.ControlledTagValueId);
        Assert.Equal("created", record.Operation);
        Assert.Equal(20, record.After.SortOrder);
    }

    [Fact]
    public async Task Rejects_value_for_marker_or_retired_definition()
    {
        var marker = ControlledDefinition();
        marker.ValueMode = TagValueMode.Marker;
        var values = new Values();
        var manager = new DefaultControlledTagValueManager(new Definitions(marker), values, new Context(), TimeProvider.System, values);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(new CreateControlledTagValueRequest
        {
            TagDefinitionId = "environment",
            CanonicalKey = "production"
        }).AsTask());
    }

    [Fact]
    public async Task Updates_only_presentation_and_order_using_a_revision()
    {
        var audit = new Audits();
        var values = new Values(audit);
        var initial = new ControlledTagValue { Id = "prod", TagDefinitionId = "environment", CanonicalKey = "production", DisplayName = "Prod", SortOrder = 10 };
        Assert.True(await values.TryAddAsync(initial));
        var manager = new DefaultControlledTagValueManager(new Definitions(ControlledDefinition()), values, new Context(), TimeProvider.System, values);

        var updated = await manager.UpdateAsync("prod", new UpdateControlledTagValueRequest { DisplayName = "Production", SortOrder = 5 }, "1");

        Assert.Equal("production", updated.Value.CanonicalKey);
        Assert.Equal("Production", updated.Value.DisplayName);
        Assert.Equal(5, updated.Value.SortOrder);
        Assert.Equal("2", updated.Revision);
    }

    private static TagDefinition ControlledDefinition() => new()
    {
        Id = "environment",
        CanonicalKey = "environment",
        DisplayName = "Environment",
        ValueMode = TagValueMode.Controlled,
        Cardinality = TagCardinality.Single
    };

    private sealed class Definitions(params TagDefinition[] definitions) : ITagDefinitionStore
    {
        public ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(definitions.SingleOrDefault(x => x.CanonicalKey == canonicalKey));
        public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(definitions.SingleOrDefault(x => x.Id == tagDefinitionId) is { } definition ? new TagDefinitionRevisionedRecord(definition, "1") : null);
        public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<TagDefinition>>(definitions);
        public ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Values(Audits? audits = null) : IControlledTagValueStore, IControlledTagValueAtomicChangeStore
    {
        private readonly Dictionary<string, (ControlledTagValue Value, long Revision)> values = new();
        public ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(values.Values.Select(x => x.Value).SingleOrDefault(x => x.TagDefinitionId == tagDefinitionId && x.CanonicalKey == canonicalKey));
        public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(values.TryGetValue(id, out var value) ? new ControlledTagValueRevisionedRecord(value.Value, value.Revision.ToString()) : null);
        public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ControlledTagValueRevisionedRecord>>([]);
        public ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default) { if (values.Values.Any(x => x.Value.TagDefinitionId == value.TagDefinitionId && x.Value.CanonicalKey == value.CanonicalKey)) return ValueTask.FromResult(false); values.Add(value.Id, (value, 1)); return ValueTask.FromResult(true); }
        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default) { if (!values.TryGetValue(value.Id, out var old)) return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.NotFound)); if (old.Revision.ToString() != expectedRevision) return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict)); values[value.Id] = (value, old.Revision + 1); return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Saved, (old.Revision + 1).ToString())); }

        public async ValueTask<ControlledTagValueCreateResult> TryAddWithinLimitAndAppendAuditAsync(
            ControlledTagValue value,
            ControlledTagValueAuditRecord audit,
            int expectedCount,
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            if (values.Count >= maximumCount)
                return ControlledTagValueCreateResult.LimitReached;
            if (!await TryAddAsync(value, cancellationToken))
                return ControlledTagValueCreateResult.Conflict;
            if (audits is not null)
                await audits.AppendAsync(audit, cancellationToken);
            return ControlledTagValueCreateResult.Created;
        }

        public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(
            ControlledTagValue value,
            string expectedRevision,
            ControlledTagValueAuditRecord audit,
            CancellationToken cancellationToken = default)
        {
            var result = await SaveWithRevisionAsync(value, expectedRevision, cancellationToken);
            if (result.Status == TagDefinitionSaveStatus.Saved && audits is not null)
                await audits.AppendAsync(audit, cancellationToken);
            return result;
        }
    }

    private sealed class Audits : IControlledTagValueAuditStore { public List<ControlledTagValueAuditRecord> Records { get; } = []; public ValueTask AppendAsync(ControlledTagValueAuditRecord record, CancellationToken cancellationToken = default) { Records.Add(record); return ValueTask.CompletedTask; } }
    private sealed class Context : ITagDefinitionAuditContext { public string Actor => "author"; public string CorrelationId => "correlation"; public string? TenantId => "tenant-a"; }
}
