using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Default host policy that permits every start which passed Runtime's structural gates.</summary>
public sealed class AllowWorkflowExecutableStartPolicy : IWorkflowExecutableStartPolicy
{
    public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(
        WorkflowExecutableStartPolicyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(WorkflowExecutableStartDecision.Allow());
    }
}
