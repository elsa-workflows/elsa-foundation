using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Core.Services;
using Xunit;

namespace Elsa.Tagging.Tests;

public class TagDefinitionManagerTests
{
    [Fact]
    public async Task Creates_an_active_workflow_definition_marker_tag_with_an_opaque_id()
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);

        var tag = await manager.CreateAsync(new CreateTagDefinitionRequest
        {
            CanonicalKey = "risk.pii",
            DisplayName = "Contains PII",
            Description = "Workflow definitions that process personal data.",
            Color = "#C2410C"
        });

        Assert.NotEmpty(tag.Id);
        Assert.Equal("risk.pii", tag.CanonicalKey);
        Assert.Equal("Contains PII", tag.DisplayName);
        Assert.Equal(TagDefinitionStatus.Active, tag.Status);
        Assert.Equal(TagDefinitionEligibility.WorkflowDefinition, tag.Eligibility);
    }

    [Fact]
    public async Task Rejects_reserved_elsa_namespace_without_explicit_host_provisioning()
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(new CreateTagDefinitionRequest
        {
            CanonicalKey = "elsa.system"
        }).AsTask());

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Host_provisioning_can_create_a_reserved_marker_tag()
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);

        var tag = await manager.CreateAsync(new CreateTagDefinitionRequest
        {
            CanonicalKey = "elsa.system",
            IsHostProvisioning = true
        });

        Assert.Equal("elsa.system", tag.CanonicalKey);
    }

    [Fact]
    public async Task Records_the_command_audit_context_for_catalog_changes()
    {
        var audit = new InMemoryAuditStore();
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), audit, new TestAuditContext(), TimeProvider.System);

        await manager.CreateAsync(new CreateTagDefinitionRequest { CanonicalKey = "risk.pii" });

        var record = Assert.Single(audit.Records);
        Assert.Equal("test-author", record.Actor);
        Assert.Equal("tenant-a", record.TenantId);
        Assert.Equal("test-correlation", record.CorrelationId);
        Assert.Equal("created", record.Operation);
        Assert.Null(record.Before);
        Assert.Equal("risk.pii", record.After.DisplayName);
        Assert.Equal(TagDefinitionStatus.Active, record.After.Status);
    }

    [Fact]
    public async Task Updates_mutable_catalog_metadata_by_stable_identity_without_changing_the_key()
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);
        var created = await manager.CreateAsync(new CreateTagDefinitionRequest { CanonicalKey = "risk.pii" });

        var updated = await manager.UpdateAsync(created.Id, new UpdateTagDefinitionRequest
        {
            DisplayName = "Personal data",
            Status = TagDefinitionStatus.Retired
        }, "1");

        Assert.Equal(created.Id, updated.Definition.Id);
        Assert.Equal("risk.pii", updated.Definition.CanonicalKey);
        Assert.Equal("Personal data", updated.Definition.DisplayName);
        Assert.Equal(TagDefinitionStatus.Retired, updated.Definition.Status);
    }

    [Fact]
    public async Task Partial_updates_distinguish_omitted_fields_from_explicit_null()
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);
        var created = await manager.CreateAsync(new CreateTagDefinitionRequest
        {
            CanonicalKey = "risk.pii",
            Description = "Sensitive data",
            Color = "#C2410C"
        });

        var retired = await manager.UpdateAsync(
            created.Id,
            new UpdateTagDefinitionRequest { Status = TagDefinitionStatus.Retired },
            "1");
        Assert.Equal("Sensitive data", retired.Definition.Description);
        Assert.Equal("#C2410C", retired.Definition.Color);

        var cleared = await manager.UpdateAsync(
            created.Id,
            new UpdateTagDefinitionRequest { Description = null, Color = null },
            "2");
        Assert.Null(cleared.Definition.Description);
        Assert.Null(cleared.Definition.Color);
    }

    [Fact]
    public async Task Records_semantic_before_and_after_values_for_catalog_updates()
    {
        var audit = new InMemoryAuditStore();
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), audit, new TestAuditContext(), TimeProvider.System);
        var created = await manager.CreateAsync(new CreateTagDefinitionRequest
        {
            CanonicalKey = "risk.pii",
            DisplayName = "Contains PII",
            Description = "Sensitive"
        });

        await manager.UpdateAsync(created.Id, new UpdateTagDefinitionRequest
        {
            DisplayName = "Personal data",
            Description = null,
            Status = TagDefinitionStatus.Retired
        }, "1");

        var record = audit.Records.Single(record => record.Operation == "updated");
        Assert.Equal("Contains PII", record.Before!.DisplayName);
        Assert.Equal("Sensitive", record.Before.Description);
        Assert.Equal("Personal data", record.After.DisplayName);
        Assert.Null(record.After.Description);
        Assert.Equal(TagDefinitionStatus.Retired, record.After.Status);
    }

    [Theory]
    [InlineData("Risk.Pii")]
    [InlineData("risk pii")]
    [InlineData("risk/pii")]
    [InlineData("1risk")]
    public async Task Rejects_noncanonical_marker_keys(string key)
    {
        var manager = new DefaultTagDefinitionManager(new InMemoryStore(), new InMemoryAuditStore(), new TestAuditContext(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(new CreateTagDefinitionRequest { CanonicalKey = key }).AsTask());
    }

    private sealed class InMemoryStore : ITagDefinitionStore
    {
        private readonly Dictionary<string, TagDefinition> definitions = new(StringComparer.Ordinal);

        public ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definitions.TryGetValue(canonicalKey, out var definition) ? Clone(definition) : null);

        public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definitions.Values.FirstOrDefault(definition => definition.Id == tagDefinitionId) is { } definition
                ? new TagDefinitionRevisionedRecord(Clone(definition), "1")
                : null);

        public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TagDefinition>>(definitions.Values.Select(Clone).ToArray());

        public ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default)
        {
            if (definitions.ContainsKey(definition.CanonicalKey))
                return ValueTask.FromResult(false);
            definitions.Add(definition.CanonicalKey, Clone(definition));
            return ValueTask.FromResult(true);
        }

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default)
        {
            definitions[definition.CanonicalKey] = Clone(definition);
            return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Saved, "2"));
        }

        private static TagDefinition Clone(TagDefinition definition) => new()
        {
            Id = definition.Id,
            CanonicalKey = definition.CanonicalKey,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Color = definition.Color,
            Status = definition.Status,
            Eligibility = definition.Eligibility,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };
    }

    private sealed class InMemoryAuditStore : ITagDefinitionAuditStore
    {
        public List<TagDefinitionAuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(TagDefinitionAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAuditContext : ITagDefinitionAuditContext
    {
        public string Actor => "test-author";
        public string CorrelationId => "test-correlation";
        public string? TenantId => "tenant-a";
    }
}
