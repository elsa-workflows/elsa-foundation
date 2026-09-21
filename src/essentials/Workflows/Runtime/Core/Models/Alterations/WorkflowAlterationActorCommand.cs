namespace Elsa.Workflows.Runtime.Core.Models.Alterations;

/// <summary>
/// Durable-safe actor payload for one leased alteration job. It carries identifiers and a claim fence only; deferred
/// envelopes remain protected in the plan record and are loaded inside the workflow actor.
/// </summary>
public sealed record WorkflowAlterationActorCommand(string JobId, string ClaimToken)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClaimToken);
    }
}

/// <summary>Actor-enqueue evidence for a deterministic alteration command.</summary>
public sealed record WorkflowAlterationActorDispatchResult(
    string JobId,
    WorkflowExecutionCommandDispatchResult Dispatch,
    WorkflowExecutionActorDescriptor Actor);
