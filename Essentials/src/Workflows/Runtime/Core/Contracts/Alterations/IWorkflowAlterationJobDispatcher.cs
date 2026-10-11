using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>Routes one claimed alteration job through the target workflow's single-writer actor mailbox.</summary>
public interface IWorkflowAlterationJobDispatcher
{
    ValueTask<WorkflowAlterationActorDispatchResult> DispatchAsync(
        WorkflowAlterationJobState job,
        CancellationToken cancellationToken = default);
}
