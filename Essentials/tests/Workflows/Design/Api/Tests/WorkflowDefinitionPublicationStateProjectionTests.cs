using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.List;
using Elsa.Workflows.Design.Api.Models;
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
    public async Task List_exposes_lookup_states_without_extra_design_reads()
    {
        var definitions = new OneDefinitionStore();
        var projections = new EmptyProjectionStore();
        var lookup = new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationState>
        {
            ["orders"] = WorkflowDefinitionPublicationState.Retracted
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
    public async Task Details_overlay_publication_state_when_the_lookup_is_present()
    {
        var reader = new WorkflowDefinitionDetailsReader(
            new EmptyVersionStore(),
            new OneDefinitionStore(),
            new EmptyDraftStore(),
            new StubPublicationStateLookup(new Dictionary<string, WorkflowDefinitionPublicationState>
            {
                ["orders"] = WorkflowDefinitionPublicationState.Retired
            }));

        var details = await reader.ReadAsync("orders", CancellationToken.None);

        Assert.Equal(WorkflowDefinitionPublicationStateFormatting.Retired, details.Definition.PublicationState);
    }

    private sealed class StubPublicationStateLookup(
        IReadOnlyDictionary<string, WorkflowDefinitionPublicationState> states) : IWorkflowDefinitionPublicationStateLookup
    {
        public IReadOnlyCollection<string>? RequestedIds { get; private set; }

        public Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationState>> ListByDefinitionIdsAsync(
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

    private sealed class EmptyVersionStore : IWorkflowDefinitionVersionStore
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
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);

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
}
