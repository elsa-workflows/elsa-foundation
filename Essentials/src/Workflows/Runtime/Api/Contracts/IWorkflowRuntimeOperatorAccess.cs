using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>
/// Request-scoped answer to "may this caller operate this run?" (spec 260 FR-416), the per-user, per-run permission
/// signal the run details carry as <c>permissions.canManage</c>. It is a display hint: the alteration endpoint still
/// enforces <c>workflows/runtime:manage</c>, and target capture still enforces the tenant. Implementations fail closed.
/// </summary>
public interface IWorkflowRuntimeOperatorAccess
{
    /// <summary>Whether the caller holds <c>workflows/runtime:manage</c> for <paramref name="workflowExecution"/>.</summary>
    ValueTask<bool> CanManageAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default);
}
