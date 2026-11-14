using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional single-owner qualification of exact selected work and the actual execution pipeline.
/// The leaf owner qualifies construction, inputs, complete effects and absence of asynchronous/child responsibilities.
/// Tree owners explicitly qualify each owned node and its normal child responsibilities, including input/effect closure.
/// Unknown/custom/script/parent-dependent profiles refuse. No default exists; routing or trusted setup is not effect qualification.
/// </summary>
public interface IRuntimeSelectedStartTargetProfile
{
    ValueTask<bool> CanExecuteAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode target,
        IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default);

    /// <summary>Explicit opt-in for every node of a selected owned tree, including its root. Legacy leaf owners deny trees.</summary>
    ValueTask<bool> CanExecuteOwnedNodeAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode selectedRoot,
        ExecutableNode ownedNode, IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
