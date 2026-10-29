using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IActivityExecutionInspectionWriter
{
    ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default);

    /// <summary>Appends an immutable fact to an existing invocation; false means unsupported or missing evidence.</summary>
    ValueTask<bool> TryAppendObservationAsync(string workflowExecutionId, string activityExecutionId,
        ActivityExecutionObservation observation, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}
