using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IActivityExecutionInspectionStore
{
    /// <summary>Whether this provider supports bounded activity/ancestry browsing.</summary>
    bool SupportsExecutionBrowsing => false;

    ValueTask<ActivityExecutionBrowsePage> BrowseAsync(ActivityExecutionBrowseQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This inspection provider does not support execution browsing.");

    ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default);
}
