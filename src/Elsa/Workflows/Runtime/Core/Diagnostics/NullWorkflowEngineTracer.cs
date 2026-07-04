using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// The default no-op <see cref="IWorkflowEngineTracer"/>, registered by the runtime composition root when the tracing
/// feature is not composed. Every method returns <c>null</c> with no allocation, so the engine hot path incurs no
/// tracing cost and its execution semantics are unchanged.
/// </summary>
public sealed class NullWorkflowEngineTracer : IWorkflowEngineTracer
{
    /// <summary>The shared instance — the tracer is stateless, so a singleton avoids per-registration allocation.</summary>
    public static readonly NullWorkflowEngineTracer Instance = new();

    /// <inheritdoc />
    public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request) => null;

    /// <inheritdoc />
    public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem) => null;

    /// <inheritdoc />
    public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem) => null;

    /// <inheritdoc />
    public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit) => null;
}
