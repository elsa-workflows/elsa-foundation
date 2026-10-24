using System.Text.Json;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.List;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowDefinitionPublicationStateProjectionTests
{
    [Fact]
    public async Task List_omits_publication_state_when_the_lookup_is_absent()
    {
        var endpoint = new Endpoint(new OneDefinitionStore(), new EmptyProjectionStore());

        var result = await endpoint.HandleAsync(
            new ListDefinitions(null, null, null, null, null, "all"),
            CancellationToken.None);

        var view = Assert.Single(result.Items);
        Assert.Null(view.PublicationState);
        Assert.Equal(1, view.VersionCount);
    }

    [Fact]
    public async Task List_omits_publication_state_when_the_null_lookup_is_injected()
    {
        var endpoint = new Endpoint(
            new OneDefinitionStore(),
            new EmptyProjectionStore(),
            new NullWorkflowDefinitionPublicationStateLookup());

        var result = await endpoint.HandleAsync(
            new ListDefinitions(null, null, null, null, null, "all"),
            CancellationToken.None);

        Assert.Null(Assert.Single(result.Items).PublicationState);
    }

    [Fact]
    public async Task List_exposes_lookup_states_without_extra_design_reads()
    {
        var definitions = new OneDefinitionStore();
        var projections = new EmptyProjectionStore();
        var lookup = new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary>
        {
            ["orders"] = new(WorkflowDefinitionPublicationState.Retracted)
        });
        var endpoint = new Endpoint(definitions, projections, lookup);

        var result = await endpoint.HandleAsync(
            new ListDefinitions(null, null, null, null, null, "all"),
            CancellationToken.None);

        var view = Assert.Single(result.Items);
        Assert.Equal(WorkflowDefinitionPublicationStateFormatting.Retracted, view.PublicationState);
        Assert.Equal(1, definitions.ReadCount);
        Assert.Equal(1, projections.ReadCount);
        Assert.Equal(["orders"], lookup.RequestedIds);
    }

    [Fact]
    public async Task List_projects_the_active_published_version_separately_from_the_latest_saved_version()
    {
        var projection = new WorkflowDefinitionListProjection(
            "orders",
            "draft-orders",
            "version-2",
            "2.0.0",
            2,
            "version-1",
            "1.0.0",
            [new("version-1", "1.0.0"), new("version-2", "2.0.0")]);
        var endpoint = new Endpoint(
            new OneDefinitionStore(),
            new FixedProjectionStore(projection),
            new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary>
            {
                ["orders"] = new(WorkflowDefinitionPublicationState.Published, "version-1")
            }));

        var view = Assert.Single((await endpoint.HandleAsync(
            new ListDefinitions(null, null, null, null, null, "all"),
            CancellationToken.None)).Items);

        Assert.Equal("2.0.0", view.LatestVersion);
        Assert.Equal("version-1", view.DraftSourceVersionId);
        Assert.Equal("1.0.0", view.DraftSourceVersion);
        Assert.Equal("version-1", view.PublishedVersionId);
        Assert.Equal("1.0.0", view.PublishedVersion);
    }

    [Fact]
    public async Task Details_overlay_publication_state_when_the_lookup_is_present()
    {
        var reader = new WorkflowDefinitionDetailsReader(
            new TwoVersionStore(),
            new OneDefinitionStore(),
            new EmptyDraftStore(),
            new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary>
            {
                ["orders"] = new(WorkflowDefinitionPublicationState.Retired)
            }));

        var details = await reader.ReadAsync("orders", CancellationToken.None);

        Assert.Equal(WorkflowDefinitionPublicationStateFormatting.Retired, details.Definition.PublicationState);
        Assert.Equal("2.0.0", details.Definition.LatestVersion);
        Assert.Equal("version-2", details.Definition.LatestVersionId);
        Assert.Equal(2, details.Definition.VersionCount);
    }

    [Fact]
    public async Task Details_resolve_draft_base_and_active_publication_to_their_exact_saved_versions()
    {
        var reader = new WorkflowDefinitionDetailsReader(
            new TwoVersionStore(),
            new OneDefinitionStore(),
            new OneDraftStore(),
            new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary>
            {
                ["orders"] = new(WorkflowDefinitionPublicationState.Published, "version-1")
            }));

        var details = await reader.ReadAsync("orders", CancellationToken.None);

        Assert.Equal("2.0.0", details.Definition.LatestVersion);
        Assert.Equal("version-1", details.Definition.DraftSourceVersionId);
        Assert.Equal("1.0.0", details.Definition.DraftSourceVersion);
        Assert.Equal("version-1", details.Definition.PublishedVersionId);
        Assert.Equal("1.0.0", details.Definition.PublishedVersion);
    }

    [Fact]
    public void Wire_omits_publication_state_when_null_and_writes_it_when_set()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var unset = new WorkflowDefinitionView("orders", "Orders", null, now, now, VersionCount: 1);
        var omitted = JsonSerializer.SerializeToElement(unset, options);
        Assert.False(omitted.TryGetProperty("publicationState", out _));
        Assert.False(omitted.TryGetProperty("publishedVersionId", out _));
        Assert.False(omitted.TryGetProperty("publishedVersion", out _));
        Assert.False(omitted.TryGetProperty("draftSourceVersionId", out _));
        Assert.False(omitted.TryGetProperty("draftSourceVersion", out _));

        var set = unset with
        {
            PublicationState = WorkflowDefinitionPublicationStateFormatting.Retracted,
            PublishedVersionId = "version-1",
            PublishedVersion = "1.0.0",
            DraftSourceVersionId = "version-2",
            DraftSourceVersion = "2.0.0"
        };
        var written = JsonSerializer.SerializeToElement(set, options);
        Assert.Equal("retracted", written.GetProperty("publicationState").GetString());
        Assert.Equal("version-1", written.GetProperty("publishedVersionId").GetString());
        Assert.Equal("1.0.0", written.GetProperty("publishedVersion").GetString());
        Assert.Equal("version-2", written.GetProperty("draftSourceVersionId").GetString());
        Assert.Equal("2.0.0", written.GetProperty("draftSourceVersion").GetString());
    }

    private sealed class StubPublicationStateLookup(
        IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary> states) : IWorkflowDefinitionPublicationStateLookup
    {
        public IReadOnlyCollection<string>? RequestedIds { get; private set; }

        public Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            RequestedIds = workflowDefinitionIds.ToArray();
            return Task.FromResult(states);
        }
    }

    private sealed class OneDefinitionStore : IWorkflowDefinitionStore
    {
        public int ReadCount { get; private set; }

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(Definition());
        }

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<WorkflowDefinition>>([Definition()]);
        }

        private static WorkflowDefinition Definition() => new()
        {
            Id = "orders",
            Name = "Orders"
        };
    }

    private sealed class EmptyProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            IReadOnlyList<WorkflowDefinitionListProjection> projections = workflowDefinitionIds
                .Select(id => new WorkflowDefinitionListProjection(id, null, "version-1", "1.0.0", 1))
                .ToArray();
            return Task.FromResult(projections);
        }
    }

    private sealed class FixedProjectionStore(WorkflowDefinitionListProjection projection) : IWorkflowDefinitionListProjectionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionListProjection>>([projection]);
    }

    private sealed class TwoVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([
                new WorkflowDefinitionVersion(definitionId, "1.0.0") { Id = "version-1" },
                new WorkflowDefinitionVersion(definitionId, "2.0.0") { Id = "version-2" }
            ]);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyDraftStore : IWorkflowDefinitionDraftStore
    {
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(null);

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(null);

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([]);

        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);

        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DraftWithLayout?>(null);
    }

    private sealed class OneDraftStore : IWorkflowDefinitionDraftStore
    {
        private static WorkflowDefinitionDraft Draft() => new()
        {
            Id = "draft-orders",
            WorkflowDefinitionId = "orders",
            SourceVersionId = "version-1",
            State = new WorkflowDefinitionState([], null, [], [], null)
        };

        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(draftId == "draft-orders" ? Draft() : null);

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(workflowDefinitionId == "orders" ? Draft() : null);

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>(workflowDefinitionId == "orders" ? [Draft()] : []);

        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);

        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DraftWithLayout?>(draftId == "draft-orders" ? new(Draft(), []) : null);
    }
}
