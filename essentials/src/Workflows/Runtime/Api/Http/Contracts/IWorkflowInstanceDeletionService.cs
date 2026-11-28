using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>Performs an authorized deletion against the shell's workflow execution provider.</summary>
public interface IWorkflowInstanceDeletionService
{
    Task<WorkflowInstanceDeletionView> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
