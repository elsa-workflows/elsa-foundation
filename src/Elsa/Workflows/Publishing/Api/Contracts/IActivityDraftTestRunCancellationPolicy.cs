using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Contracts;

public interface IActivityDraftTestRunCancellationPolicy
{
    ValueTask<ActivityDraftTestRunCancellationDecision> EvaluateAsync(
        ActivityDraftTestRunReceipt receipt,
        WorkflowExecutionState? execution,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityDraftTestRunCancellationDecision(
    bool CapabilityAdvertised,
    bool IsAllowed,
    string? Reason);
