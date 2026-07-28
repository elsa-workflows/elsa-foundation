using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IActivityExecutionInspectionStore
{
    ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default);
}
