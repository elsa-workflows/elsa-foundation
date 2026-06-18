using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Workflows.Agent.Models;

namespace Elsa.Foundation.Workflows.Agent.Contracts;

public interface IWorkflowAgentContextProvider
{
    Task<WorkflowAgentContext> GetContextAsync(WorkflowAgentContextRequest request, CancellationToken cancellationToken = default);
}

public interface IWorkflowRevisionProvider
{
    Task<string> GetCurrentRevisionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);
}

public interface IWorkflowChangePermissionEvaluator
{
    Task<bool> CanProposeChangeAsync(string actorId, string workflowDefinitionId, CancellationToken cancellationToken = default);
}

public interface IWorkflowChangeProposalService
{
    Task<AgentResult<AgentActionProposal>> ProposeAsync(WorkflowChangeProposalRequest request, CancellationToken cancellationToken = default);
}
