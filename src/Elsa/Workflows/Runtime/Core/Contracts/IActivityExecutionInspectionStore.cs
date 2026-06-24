using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IActivityExecutionInspectionStore
{
    ValueTask<ActivityExecutionInspectionProjection> SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ActivityExecutionInspectionProjection>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ActivityExecutionInspectionProjection>> ListByAuthoredActivityIdAsync(string workflowExecutionId, string authoredActivityId, CancellationToken cancellationToken = default);
}
