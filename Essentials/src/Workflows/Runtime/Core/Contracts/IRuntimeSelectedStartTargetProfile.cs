using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional single-owner qualification of an exact selected terminal leaf and the actual execution pipeline.
/// The owner must qualify construction, inputs, complete effects and absence of asynchronous/child responsibilities.
/// Unknown/custom/script/parent-dependent profiles refuse. No default exists; routing or trusted setup is not effect qualification.
/// </summary>
public interface IRuntimeSelectedStartTargetProfile
{
    ValueTask<bool> CanExecuteAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode target,
        IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default);
}
