using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

public interface IWorkflowDefinitionTagStore
{
    Task<WorkflowDefinitionTagSet> GetAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkflowDefinitionTagSet>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionTagReplaceResult> ReplaceManualAsync(
        ReplaceWorkflowDefinitionManualTags request,
        CancellationToken cancellationToken = default);
}
