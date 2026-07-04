using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Test command executor that records the envelope IDs it processed and returns a configurable process result. Standing
/// in for the real drain, it lets tests assert exactly which commands ran on which node without a live workflow engine.
/// </summary>
internal sealed class RecordingCommandExecutor : IWorkflowExecutionCommandExecutor
{
    private readonly ConcurrentQueue<string> _processed = new();
    private readonly Func<WorkflowExecutionCommandEnvelope, WorkflowExecutionCommandProcessResult> _resultFactory;

    public RecordingCommandExecutor(Func<WorkflowExecutionCommandEnvelope, WorkflowExecutionCommandProcessResult>? resultFactory = null)
    {
        _resultFactory = resultFactory ?? (_ => WorkflowExecutionCommandProcessResult.NoDrain);
    }

    public IReadOnlyList<string> Processed => _processed.ToArray();

    public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _processed.Enqueue(envelope.EnvelopeId);
        return new ValueTask<WorkflowExecutionCommandProcessResult>(_resultFactory(envelope));
    }
}
