using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Opt-in, context-aware variant of <see cref="IWorkflowSchedulerWorkHandler"/> for handlers that have been decomposed
/// into the pipeline (ADR 0029, Move 2). The <c>Invoke</c>-slot middleware invokes this method with the running
/// dispatch's pipeline context, threaded <em>explicitly</em> (no ambient accessor), so the handler can stage phase
/// results (e.g. a checkpoint commit) on the workspace for later slots to apply. A handler still implements the plain
/// <see cref="IWorkflowSchedulerWorkHandler"/> for direct (no-pipeline) dispatch, which commits inline as before.
/// </summary>
public interface IRuntimePipelineWorkHandler
{
    ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default);
}
