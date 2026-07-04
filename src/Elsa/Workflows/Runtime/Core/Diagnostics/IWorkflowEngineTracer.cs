using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// Starts engine-phase tracing spans for the four workflow runtime hot-path phases (MS-9): drain cycle, dispatch,
/// activity execution, and checkpoint commit. Implementations MUST be allocation-free and side-effect-free when
/// tracing is disabled (return <c>null</c>), so instrumenting the fenced drain/commit path never changes execution
/// semantics.
/// </summary>
/// <remarks>
/// <para>
/// A returned <see cref="Activity"/> (when non-null) is owned by the caller and disposed via <c>using</c> at the end
/// of the phase. Parent-child nesting is established automatically through <see cref="Activity.Current"/> — callers do
/// not thread a parent explicitly. Callers add outcome tags after the fact with <c>activity?.SetTag(...)</c>, which is
/// a no-op when the activity is null.
/// </para>
/// <para>
/// This is a single-implementation replacement contract: the default <see cref="NullWorkflowEngineTracer"/> is
/// registered by the runtime composition root; composing <c>WorkflowsRuntimeTracingFeature</c> replaces it with the
/// <see cref="ActivitySourceWorkflowEngineTracer"/> that emits real spans (which still cost nothing until a listener
/// attaches).
/// </para>
/// </remarks>
public interface IWorkflowEngineTracer
{
    /// <summary>Starts a span for a scheduler drain cycle, or returns <c>null</c> when tracing is inactive.</summary>
    Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request);

    /// <summary>Starts a span for dispatching a single work item, or returns <c>null</c> when tracing is inactive.</summary>
    Activity? StartDispatch(RuntimeSchedulerWorkItem workItem);

    /// <summary>Starts a span for executing a work item's handler (activity execution), or returns <c>null</c> when tracing is inactive.</summary>
    Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem);

    /// <summary>Starts a span for committing a runtime checkpoint, or returns <c>null</c> when tracing is inactive.</summary>
    Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit);
}
