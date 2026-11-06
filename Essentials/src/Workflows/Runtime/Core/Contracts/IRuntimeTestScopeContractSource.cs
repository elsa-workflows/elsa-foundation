using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Model-owned contribution to the named runtime test-scope contract collection event.</summary>
public interface IRuntimeTestScopeContractSource
{
    ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable,
        ExecutableNode scopeNode, VariableFrameKind kind, CancellationToken cancellationToken = default);
}
