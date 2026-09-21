using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class DefaultActivityDraftTestRunCancellationPolicy : IActivityDraftTestRunCancellationPolicy
{
    public ValueTask<ActivityDraftTestRunCancellationDecision> EvaluateAsync(
        ActivityDraftTestRunReceipt receipt,
        WorkflowExecutionState? execution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = execution switch
        {
            null => new(true, false, "Runtime Evidence is not available yet."),
            { Status: var status } when status.IsTerminal() =>
                new ActivityDraftTestRunCancellationDecision(true, false, "The Run is already terminal."),
            _ => new ActivityDraftTestRunCancellationDecision(true, true, null)
        };
        return ValueTask.FromResult(decision);
    }
}
