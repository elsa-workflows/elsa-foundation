using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.List;

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
            new ListDefinitions(null, null, null, null, null, "all"),
            CancellationToken.None)).Items.ToArray();

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

    [Theory]
    [InlineData(null, 13)]
    [InlineData("active", 13)]
    [InlineData("deleted", 12)]
    [InlineData("all", 25)]
    public async Task Definition_list_honors_active_deleted_and_all_scopes(string? state, int expectedCount)
    {
        var fixture = new ProjectionFixture(25);
        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(null, null, null, null, null, state),
            CancellationToken.None);

        Assert.Equal(expectedCount, result.Items.Count);
        Assert.InRange(fixture.TotalReadCount, 1, 3);
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
        private readonly CountingProjectionStore _projections;

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
            _projections = new CountingProjectionStore();
        }

        public int TotalReadCount => _definitions.ReadCount + _projections.ReadCount;

        public Handler CreateHandler()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWorkflowDefinitionStore>(_definitions)
                .AddSingleton<IWorkflowDefinitionListProjectionStore>(_projections)
                .BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<Handler>(services);
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

    private sealed class CountingProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            IReadOnlyList<WorkflowDefinitionListProjection> projections = workflowDefinitionIds
                .Select(workflowDefinitionId =>
                {
                    var ordinal = workflowDefinitionId["definition-".Length..];
                    return new WorkflowDefinitionListProjection(
                        workflowDefinitionId,
                        $"draft-{ordinal}",
                        $"version-{ordinal}-2",
                        "2.0.0",
                        2);
                })
                .ToArray();
            return Task.FromResult(projections);
        }
    }
}
