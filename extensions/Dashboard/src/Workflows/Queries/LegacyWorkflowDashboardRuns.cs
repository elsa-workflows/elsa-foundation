using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;

namespace Elsa.Workflows.Dashboard.Queries;

internal sealed class LegacyWorkflowDashboardRuns(IWorkflowExecutionStateStore executions) : IWorkflowRetainedRunSource
{
    public async IAsyncEnumerable<WorkflowExecutionState?> ReadAsync(string tenant, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            // History windows on this store use effective update time. The query evaluates its actual start window separately.
            var page = await executions.QueryPageAsync(new(100, TenantId: tenant, Cursor: cursor), cancellationToken);
            if (page.Items.Count > 100 || page.HasNext != (page.NextCursor is not null))
                throw WorkflowDashboardQuerySource.InvalidSource();
            foreach (var state in page.Items) yield return state;
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) throw WorkflowDashboardQuerySource.InvalidSource();
        } while (cursor is not null);
    }
}
