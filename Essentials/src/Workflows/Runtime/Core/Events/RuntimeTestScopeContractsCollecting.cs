using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Events;

/// <summary>Sequential model-owned scope-contract collection; exactly one matching contract is required.</summary>
public sealed class RuntimeTestScopeContractsCollecting(WorkflowExecutable executable, ExecutableNode scopeNode, VariableFrameKind kind) : IEvent
{
    public WorkflowExecutable Executable { get; } = executable;
    public ExecutableNode ScopeNode { get; } = scopeNode;
    public VariableFrameKind Kind { get; } = kind;
    public ICollection<RuntimeTestScopeContract> Contracts { get; } = [];
}
