using Elsa.Workflows.Runtime.Core.Contracts.Alterations;

namespace Elsa.Workflows.Runtime.Core.Models.Alterations;

/// <summary>One handler's safe preflight decision; rejected decisions stop the ordered plan before any mutation.</summary>
public sealed record WorkflowAlterationPreflightResult
{
    private WorkflowAlterationPreflightResult(bool isAccepted, WorkflowAlterationSafeFailure? failure)
    {
        IsAccepted = isAccepted;
        Failure = failure;
    }

    public bool IsAccepted { get; }
    public WorkflowAlterationSafeFailure? Failure { get; }

    public static WorkflowAlterationPreflightResult Accepted() => new(true, null);

    public static WorkflowAlterationPreflightResult Rejected(string code, string? message = null) =>
        new(false, new WorkflowAlterationSafeFailure(code, message));
}

/// <summary>Context shared with one handler while preflighting an ordered alteration job.</summary>
public sealed record WorkflowAlterationPreflightContext(
    WorkflowAlterationJobState Job,
    WorkflowAlterationEnvelope Envelope,
    int Ordinal,
    IWorkflowAlterationProjectedState ProjectedState,
    IWorkflowAlterationStagingWorkspace Staging);

/// <summary>Actor-side input for one claimed target job. Envelopes are plaintext only inside this execution boundary.</summary>
public sealed record WorkflowAlterationJobExecutionRequest(
    WorkflowAlterationJobState Job,
    IReadOnlyList<WorkflowAlterationEnvelope> Envelopes,
    IWorkflowAlterationProjectedState ProjectedState)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Job);
        ArgumentNullException.ThrowIfNull(Envelopes);
        ArgumentNullException.ThrowIfNull(ProjectedState);
        if (Job.Status != WorkflowAlterationJobStatus.Running || Job.Claim is null)
            throw new ArgumentException("Only a claimed running alteration job may enter actor execution.", nameof(Job));
        if (Envelopes.Count == 0)
            throw new ArgumentException("An alteration job must contain at least one envelope.", nameof(Envelopes));
    }
}

/// <summary>Terminal actor result, including whether durable checkpoint evidence reconciled an acknowledgement loss.</summary>
public sealed record WorkflowAlterationJobExecutionResult(
    WorkflowAlterationJobState Job,
    bool ReconciledAfterAcknowledgementLoss);
