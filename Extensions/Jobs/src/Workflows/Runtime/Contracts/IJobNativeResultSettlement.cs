using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Contracts;

/// <summary>Completes Jobs responsibility before an owning model's existing native-result source is delivered.</summary>
public interface IJobNativeResultSettlement
{
    /// <summary>Confirms exact selected-result acknowledgement or positive proof that this native worker never admitted a job.</summary>
    /// <exception cref="JobException">The exact retained native, sender, selected-result or model consumption proof cannot currently be confirmed.</exception>
    ValueTask SettleWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default);
}
