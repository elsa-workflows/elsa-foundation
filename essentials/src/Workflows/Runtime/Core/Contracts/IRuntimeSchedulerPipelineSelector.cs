using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Selects the runtime execution pipeline (workflow vs activity) for a drained scheduler work item, using the same
/// discriminator the scheduler work handlers' <c>CanHandle</c> use: the command kind, plus the completion kind on the
/// payload to disambiguate <see cref="WorkflowExecutionCommandKind.CompleteActivity"/> (parent-completion evaluation
/// runs under the activity pipeline; routing runs under the workflow pipeline).
/// </summary>
public interface IRuntimeSchedulerPipelineSelector
{
    /// <summary>Returns the pipeline kind for <paramref name="workItem"/>. Never throws.</summary>
    RuntimePipelineKind Select(RuntimeSchedulerWorkItem workItem);
}
