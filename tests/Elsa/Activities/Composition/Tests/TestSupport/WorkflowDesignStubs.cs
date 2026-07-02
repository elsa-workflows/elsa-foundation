using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Composition.Tests.TestSupport;

/// <summary>
/// Shared in-memory fakes and builders for the Workflows.Design read ports, so the Composition
/// design-side tests don't each re-hand-roll a stub store + entity factory (global CLAUDE.md: DRY /
/// clean tests). Unused store members throw — the tests exercise only the list ports the adapter uses.
/// </summary>
internal static class WorkflowDesignData
{
    public static WorkflowDefinition Definition(string id, string name = "Workflow", string? description = null, DateTimeOffset? deletedAt = null) =>
        new() { Id = id, Name = name, Description = description, DeletedAt = deletedAt };

    public static WorkflowDefinitionVersion Version(
        string definitionId,
        string id,
        string version,
        bool? usableAsActivity,
        string? category = null,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null) =>
        new(definitionId, version)
        {
            Id = id,
            State = new WorkflowDefinitionState(
                Variables: [],
                RootActivity: null,
                Inputs: inputs ?? [],
                Outputs: outputs ?? [],
                WorkflowActivityOptions: new WorkflowActivityOptions(usableAsActivity, null, category),
                StrategyOptions: null),
        };
}

internal sealed class StubDefinitionStore(params WorkflowDefinition[] definitions) : IWorkflowDefinitionStore
{
    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkflowDefinition>>(definitions);

    public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

internal sealed class StubVersionStore(params WorkflowDefinitionVersion[] versions) : IWorkflowDefinitionVersionStore
{
    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(versions.Where(v => v.DefinitionId == definitionId).ToArray());

    public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
