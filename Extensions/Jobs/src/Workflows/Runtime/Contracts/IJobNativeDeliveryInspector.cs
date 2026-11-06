using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Contracts;

/// <summary>Reads exact native consumed-result evidence for an optional model bridge, without interpreting its private model document.</summary>
public interface IJobNativeDeliveryInspector
{
    /// <summary>Returns null for an unowned/live unconsumed worker or positively proved native-only input failure/owner cancellation.</summary>
    /// <exception cref="JobException">An owned binding is malformed or its selected result, final attempt or permission cannot be proved.</exception>
    ValueTask<JobNativeDeliveryEvidence?> InspectWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default);
}
