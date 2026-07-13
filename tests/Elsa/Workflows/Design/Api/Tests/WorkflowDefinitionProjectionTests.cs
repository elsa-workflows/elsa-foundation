using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

/// <summary>
/// RED query-budget contract for FR-029. The list response must be a Studio-ready aggregate projection,
/// and the number of persistence reads must remain bounded as the number of definitions grows.
/// </summary>
public sealed class WorkflowDefinitionProjectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public async Task Definition_list_projects_draft_latest_version_count_and_deletion_with_a_bounded_read_budget(int definitionCount)
    {
        var fixture = new ProjectionFixture(definitionCount);
        var handler = fixture.CreateHandler();

        var result = (await handler.Handle(
            new ListDefinitions(null, null, null, null, null),
            CancellationToken.None)).ToArray();

        Assert.Equal(definitionCount, result.Length);
        Assert.InRange(fixture.TotalReadCount, 1, 3);
        foreach (var view in result)
        {
            var ordinal = int.Parse(view.Id["definition-".Length..]);
            Assert.Equal($"draft-{ordinal}", RequiredProperty<string>(view, "DraftId"));
            Assert.Equal($"version-{ordinal}-2", RequiredProperty<string>(view, "LatestVersionId"));
            Assert.Equal("2.0.0", RequiredProperty<string>(view, "LatestVersion"));
            Assert.Equal(2, RequiredProperty<int>(view, "VersionCount"));
            Assert.Equal(ordinal % 2 == 0, Property(view, "DeletedAt") is not null);
        }
    }

    private static T RequiredProperty<T>(WorkflowDefinitionView view, string name) =>
        Property(view, name) is T value
            ? value
            : throw new Xunit.Sdk.XunitException($"WorkflowDefinitionView must expose populated '{name}' in the aggregate list projection.");

    private static object? Property(WorkflowDefinitionView view, string name) =>
        view.GetType().GetProperty(name)?.GetValue(view);

    private sealed class ProjectionFixture
    {
        private readonly CountingDefinitionStore _definitions;
        private readonly CountingDraftStore _drafts;
        private readonly CountingVersionStore _versions;

        public ProjectionFixture(int count)
        {
            var definitions = Enumerable.Range(1, count)
                .Select(ordinal => new WorkflowDefinition
                {
                    Id = $"definition-{ordinal}",
                    Name = $"Workflow {ordinal}",
                    DeletedAt = ordinal % 2 == 0 ? DateTimeOffset.UnixEpoch.AddDays(ordinal) : null
                })
                .ToArray();
            _definitions = new CountingDefinitionStore(definitions);
            _drafts = new CountingDraftStore();
            _versions = new CountingVersionStore();
        }

        public int TotalReadCount => _definitions.ReadCount + _drafts.ReadCount + _versions.ReadCount;

        public ListDefinitionsRequestHandler CreateHandler()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWorkflowDefinitionStore>(_definitions)
                .AddSingleton<IWorkflowDefinitionDraftStore>(_drafts)
                .AddSingleton<IWorkflowDefinitionVersionStore>(_versions)
                .BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<ListDefinitionsRequestHandler>(services);
        }
    }

    private sealed class CountingDefinitionStore(IReadOnlyList<WorkflowDefinition> definitions) : IWorkflowDefinitionStore
    {
        public int ReadCount { get; private set; }
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(definitions);
        }
    }

    private sealed class CountingDraftStore : IWorkflowDefinitionDraftStore
    {
        public int ReadCount { get; private set; }
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var ordinal = workflowDefinitionId["definition-".Length..];
            return Task.FromResult<WorkflowDefinitionDraft?>(new WorkflowDefinitionDraft
            {
                Id = $"draft-{ordinal}",
                WorkflowDefinitionId = workflowDefinitionId,
                State = EmptyState()
            });
        }

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CountingVersionStore : IWorkflowDefinitionVersionStore
    {
        public int ReadCount { get; private set; }
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var ordinal = definitionId["definition-".Length..];
            IReadOnlyList<WorkflowDefinitionVersion> versions =
            [
                new(definitionId, "1.0.0") { Id = $"version-{ordinal}-1", State = EmptyState() },
                new(definitionId, "2.0.0") { Id = $"version-{ordinal}-2", State = EmptyState() }
            ];
            return Task.FromResult(versions);
        }

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static WorkflowDefinitionState EmptyState() => new([], null, [], [], null, null);
}
