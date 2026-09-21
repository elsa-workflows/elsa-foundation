using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// <see cref="IWorkflowDefinitionVersionStore"/> that serves exactly one version (with its definition attached).
/// </summary>
internal sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
{
    public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
        version.Id == versionId
            ? Task.FromResult(version)
            : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

    public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
