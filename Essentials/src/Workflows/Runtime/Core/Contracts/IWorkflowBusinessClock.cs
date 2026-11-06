using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Context-aware business time; it never replaces the host's infrastructure TimeProvider.</summary>
public interface IWorkflowBusinessClock
{
    ValueTask<WorkflowBusinessTime> ReadAsync(ActivityExecutionContext context,
        CancellationToken cancellationToken = default);
}
