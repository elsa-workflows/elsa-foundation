using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class NoopWorkflowExecutionCommandProcessor : IWorkflowExecutionCommandProcessor
{
    public static readonly NoopWorkflowExecutionCommandProcessor Instance = new();

    public NoopWorkflowExecutionCommandProcessor()
    {
    }

    public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain);
    }

    public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);

        return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain);
    }
}
