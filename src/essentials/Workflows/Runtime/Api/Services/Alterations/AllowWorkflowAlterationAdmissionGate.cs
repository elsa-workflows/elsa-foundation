using Elsa.Workflows.Runtime.Api.Contracts.Alterations;

namespace Elsa.Workflows.Runtime.Api.Services.Alterations;

/// <summary>Default admission policy; hosts replace it when a durable queue has bounded pre-admission capacity.</summary>
public sealed class AllowWorkflowAlterationAdmissionGate : IWorkflowAlterationAdmissionGate
{
    public ValueTask<WorkflowAlterationAdmissionDecision> EvaluateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(WorkflowAlterationAdmissionDecision.Accepted);
}
