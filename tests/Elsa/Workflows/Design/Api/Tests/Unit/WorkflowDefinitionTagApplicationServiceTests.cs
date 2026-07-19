using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowDefinitionTagApplicationServiceTests
{
    [Fact]
    public async Task Service_without_durable_tagging_dependencies_fails_closed()
    {
        var service = new WorkflowDefinitionTagApplicationService(
            new FakeDefinitionStore(new() { Id = "workflow-1", Name = "Workflow" }),
            new AuthorizationContext(),
            new CapturingPublisher());

        Assert.False(service.IsAvailable);
        await Assert.ThrowsAsync<WorkflowDefinitionTaggingUnavailableException>(() =>
            service.GetAsync("workflow-1"));
        await Assert.ThrowsAsync<WorkflowDefinitionTaggingUnavailableException>(() =>
            service.ReplaceAsync("workflow-1", WorkflowDefinitionTagRevision.Initial, []));
    }

    [Fact]
    public async Task Controlled_assignment_requires_durable_controlled_value_persistence()
    {
        var service = new WorkflowDefinitionTagApplicationService(
            new FakeDefinitionStore(new()
            {
                Id = "workflow-1",
                Name = "Workflow",
                TenantId = "tenant-a"
            }),
            new AuthorizationContext(),
            new CapturingPublisher(),
            new FakeTagStore(),
            Catalog(Controlled("tag-environment")),
            new DurableCatalogPersistence(),
            Values(ActiveValue("value-production", "tag-environment")));

        await Assert.ThrowsAsync<WorkflowDefinitionTaggingUnavailableException>(() =>
            service.ReplaceAsync(
                "workflow-1",
                WorkflowDefinitionTagRevision.Initial,
                [],
                [new("tag-environment", "value-production")]));
    }

    [Fact]
    public async Task Marker_only_replacement_cannot_delete_an_existing_controlled_assignment_without_its_catalog()
    {
        var tags = new FakeTagStore
        {
            Current = new(
                "workflow-1",
                "tenant-a",
                WorkflowDefinitionTagRevision.Initial,
                [WorkflowDefinitionTagAssertion.Manual(new WorkflowDefinitionControlledTagValue(
                    "tag-environment",
                    "value-production"))])
        };
        var service = new WorkflowDefinitionTagApplicationService(
            new FakeDefinitionStore(new()
            {
                Id = "workflow-1",
                Name = "Workflow",
                TenantId = "tenant-a"
            }),
            new AuthorizationContext(),
            new CapturingPublisher(),
            tags,
            Catalog(Active("tag-priority"), Controlled("tag-environment")),
            new DurableCatalogPersistence(),
            Values(ActiveValue("value-production", "tag-environment")));

        await Assert.ThrowsAsync<WorkflowDefinitionTaggingUnavailableException>(() =>
            service.ReplaceAsync(
                "workflow-1",
                WorkflowDefinitionTagRevision.Initial,
                ["tag-priority"],
                []));

        Assert.False(tags.Saved);
    }

    [Fact]
    public async Task Replace_validates_catalog_commits_manual_slice_and_publishes_after_save()
    {
        var tags = new FakeTagStore();
        var events = new CapturingPublisher(() => Assert.True(tags.Saved));
        var service = CreateService(tags, events, Catalog(Active("tag-a")));

        var result = await service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a", "tag-a"]);

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Saved, result.Status);
        Assert.Equal(["tag-a"], tags.LastRequest!.TagDefinitionIds);
        Assert.Equal("author-1", tags.LastRequest.ActorId);
        Assert.Equal("correlation-1", tags.LastRequest.CorrelationId);
        var changed = Assert.IsType<WorkflowDefinitionTagsChanged>(Assert.Single(events.Events));
        Assert.Equal(["tag-a"], changed.AddedTagDefinitionIds);
        Assert.Empty(changed.RemovedTagDefinitionIds);
    }

    [Fact]
    public async Task Replace_rejects_retired_catalog_values_before_persistence()
    {
        var tags = new FakeTagStore();
        var service = CreateService(tags, new CapturingPublisher(), Catalog(Retired("tag-a")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"]));

        Assert.False(tags.Saved);
    }

    [Fact]
    public async Task Replace_retains_an_already_assigned_retired_catalog_value_but_does_not_add_one()
    {
        var tags = new FakeTagStore
        {
            Current = new("workflow-1", "tenant-a", WorkflowDefinitionTagRevision.Initial, [WorkflowDefinitionTagAssertion.Manual("tag-a")])
        };
        var service = CreateService(tags, new CapturingPublisher(), Catalog(Retired("tag-a")));

        var result = await service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"]);

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Saved, result.Status);
        Assert.True(tags.Saved);
    }

    [Fact]
    public async Task Conflict_does_not_publish()
    {
        var tags = new FakeTagStore { Conflict = true };
        var events = new CapturingPublisher();
        var service = CreateService(tags, events, Catalog(Active("tag-a")));

        var result = await service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"]);

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Conflict, result.Status);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task Replace_accepts_one_active_controlled_value_and_publishes_its_stable_identity()
    {
        var tags = new FakeTagStore();
        var events = new CapturingPublisher();
        var service = CreateService(
            tags,
            events,
            Catalog(Controlled("tag-environment")),
            values: Values(ActiveValue("value-production", "tag-environment")));

        var result = await service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-environment", "value-production")]);

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Saved, result.Status);
        var assertion = Assert.Single(tags.LastRequest!.ControlledValues!);
        Assert.Equal("tag-environment", assertion.TagDefinitionId);
        Assert.Equal("value-production", assertion.ControlledValueId);
        var changed = Assert.IsType<WorkflowDefinitionTagsChanged>(Assert.Single(events.Events));
        Assert.Equal(["value-production"], changed.AddedControlledValueIds);
    }

    [Fact]
    public async Task Replace_rejects_different_controlled_values_for_one_single_definition_before_store_mutation()
    {
        var tags = new FakeTagStore();
        var service = CreateService(
            tags,
            new CapturingPublisher(),
            Catalog(Controlled("tag-environment")),
            values: Values(
                ActiveValue("value-development", "tag-environment"),
                ActiveValue("value-production", "tag-environment")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-environment", "value-development"), new("tag-environment", "value-production")]));

        Assert.False(tags.Saved);
    }

    [Fact]
    public async Task Replace_rejects_a_controlled_value_owned_by_another_definition_before_store_mutation()
    {
        var tags = new FakeTagStore();
        var service = CreateService(
            tags,
            new CapturingPublisher(),
            Catalog(Controlled("tag-environment"), Controlled("tag-region")),
            values: Values(ActiveValue("value-production", "tag-environment")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-region", "value-production")]));

        Assert.False(tags.Saved);
    }

    [Fact]
    public async Task Retired_controlled_definition_only_allows_retaining_the_exact_manual_value()
    {
        var retiredDefinition = Controlled("tag-environment");
        retiredDefinition.Status = TagDefinitionStatus.Retired;
        var values = Values(
            ActiveValue("value-production", "tag-environment"),
            ActiveValue("value-staging", "tag-environment"));
        var retainedTags = new FakeTagStore
        {
            Current = new(
                "workflow-1",
                "tenant-a",
                WorkflowDefinitionTagRevision.Initial,
                [WorkflowDefinitionTagAssertion.Manual(new WorkflowDefinitionControlledTagValue(
                    "tag-environment",
                    "value-production"))])
        };
        var retainedService = CreateService(
            retainedTags,
            new CapturingPublisher(),
            Catalog(retiredDefinition),
            values);

        var retained = await retainedService.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-environment", "value-production")]);

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Saved, retained.Status);

        var changedTags = new FakeTagStore
        {
            Current = new(
                "workflow-1",
                "tenant-a",
                WorkflowDefinitionTagRevision.Initial,
                [WorkflowDefinitionTagAssertion.Manual(new WorkflowDefinitionControlledTagValue(
                    "tag-environment",
                    "value-production"))])
        };
        var changedService = CreateService(
            changedTags,
            new CapturingPublisher(),
            Catalog(retiredDefinition),
            values);
        await Assert.ThrowsAsync<ArgumentException>(() => changedService.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-environment", "value-staging")]));
        Assert.False(changedTags.Saved);

        var policyTags = new FakeTagStore
        {
            Current = new(
                "workflow-1",
                "tenant-a",
                WorkflowDefinitionTagRevision.Initial,
                [new(
                    "tag-environment",
                    "policy",
                    "policy-1",
                    "value-production")])
        };
        var policyService = CreateService(
            policyTags,
            new CapturingPublisher(),
            Catalog(retiredDefinition),
            values);
        await Assert.ThrowsAsync<ArgumentException>(() => policyService.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            [],
            [new("tag-environment", "value-production")]));
        Assert.False(policyTags.Saved);
    }

    [Fact]
    public async Task Deleted_definition_is_readable_but_never_assignable()
    {
        var tags = new FakeTagStore();
        var service = CreateService(
            tags,
            new CapturingPublisher(),
            Catalog(Active("tag-a")),
            deleted: true);

        var view = await service.GetAsync("workflow-1");

        Assert.False(view.CanAssign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplaceAsync(
            "workflow-1",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"]));
    }

    private static WorkflowDefinitionTagApplicationService CreateService(
        FakeTagStore tags,
        CapturingPublisher events,
        ITagDefinitionStore catalog,
        IControlledTagValueStore? values = null,
        bool deleted = false) =>
        new(
            new FakeDefinitionStore(new()
            {
                Id = "workflow-1",
                Name = "Workflow",
                TenantId = "tenant-a",
                DeletedAt = deleted ? DateTimeOffset.UtcNow : null
            }),
            new AuthorizationContext(),
            events,
            tags,
            catalog,
            new DurableCatalogPersistence(),
            values,
            values is null ? null : new DurableControlledValuePersistence());

    private static ITagDefinitionStore Catalog(params TagDefinition[] definitions) =>
        new FakeCatalog(definitions);

    private static TagDefinition Active(string id) => new()
    {
        Id = id,
        CanonicalKey = id,
        DisplayName = id,
        Status = TagDefinitionStatus.Active,
        Eligibility = TagDefinitionEligibility.WorkflowDefinition
    };

    private static TagDefinition Retired(string id)
    {
        var result = Active(id);
        result.Status = TagDefinitionStatus.Retired;
        return result;
    }

    private static TagDefinition Controlled(string id)
    {
        var result = Active(id);
        result.ValueMode = TagValueMode.Controlled;
        result.Cardinality = TagCardinality.Single;
        return result;
    }

    private static ControlledTagValue ActiveValue(string id, string tagDefinitionId) => new()
    {
        Id = id,
        TagDefinitionId = tagDefinitionId,
        CanonicalKey = id,
        DisplayName = id,
        Status = TagDefinitionStatus.Active
    };

    private static IControlledTagValueStore Values(params ControlledTagValue[] values) => new FakeControlledValues(values);

    private sealed class AuthorizationContext : IWorkflowDefinitionTagAuthorizationContext
    {
        public string ActorId => "author-1";
        public string CorrelationId => "correlation-1";
        public bool CanAssign => true;
    }

    private sealed class DurableCatalogPersistence : ITagDefinitionCatalogPersistence;
    private sealed class DurableControlledValuePersistence : IControlledTagValueCatalogPersistence;

    private sealed class FakeDefinitionStore(WorkflowDefinition definition) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definition);
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinition?>(id == definition.Id ? definition : null);
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>([definition]);
    }

    private sealed class FakeTagStore : IWorkflowDefinitionTagStore
    {
        public bool Conflict { get; init; }
        public bool Saved { get; private set; }
        public ReplaceWorkflowDefinitionManualTags? LastRequest { get; private set; }
        public WorkflowDefinitionTagSet Current { get; set; } =
            new("workflow-1", "tenant-a", WorkflowDefinitionTagRevision.Initial, []);

        public Task<WorkflowDefinitionTagSet> GetAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<IReadOnlyCollection<WorkflowDefinitionTagSet>> ListByDefinitionIdsAsync(IReadOnlyCollection<string> workflowDefinitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<WorkflowDefinitionTagSet>>([Current]);

        public Task<WorkflowDefinitionTagReplaceResult> ReplaceManualAsync(ReplaceWorkflowDefinitionManualTags request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Conflict)
                return Task.FromResult(new WorkflowDefinitionTagReplaceResult(
                    WorkflowDefinitionTagReplaceStatus.Conflict,
                    CurrentRevision: Current.Revision));
            Saved = true;
            Current = new(
                request.WorkflowDefinitionId,
                request.TenantId,
                WorkflowDefinitionTagRevision.FromVersion(1),
                request.TagDefinitionIds.Select(WorkflowDefinitionTagAssertion.Manual)
                    .Concat((request.ControlledValues ?? []).Select(WorkflowDefinitionTagAssertion.Manual))
                    .ToArray());
            return Task.FromResult(new WorkflowDefinitionTagReplaceResult(
                WorkflowDefinitionTagReplaceStatus.Saved,
                Current));
        }
    }

    private sealed class FakeCatalog(IEnumerable<TagDefinition> definitions) : ITagDefinitionStore
    {
        private readonly Dictionary<string, TagDefinition> _items =
            definitions.ToDictionary(x => x.Id, StringComparer.Ordinal);

        public ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.Values.SingleOrDefault(x => x.CanonicalKey == canonicalKey));
        public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.TryGetValue(tagDefinitionId, out var item) ? new TagDefinitionRevisionedRecord(item, "tag:1") : null);
        public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TagDefinition>>(_items.Values.ToArray());
        public ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeControlledValues(IEnumerable<ControlledTagValue> values) : IControlledTagValueStore
    {
        private readonly Dictionary<string, ControlledTagValue> _items = values.ToDictionary(value => value.Id, StringComparer.Ordinal);

        public ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.Values.SingleOrDefault(value => value.TagDefinitionId == tagDefinitionId && value.CanonicalKey == canonicalKey));

        public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.TryGetValue(controlledTagValueId, out var value)
                ? new ControlledTagValueRevisionedRecord(value, "value:1")
                : null);

        public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlledTagValueRevisionedRecord>>([]);

        public ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingPublisher(Action? beforePublish = null) : IDeferredEventPublisher
    {
        public List<IEvent> Events { get; } = [];
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            beforePublish?.Invoke();
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
