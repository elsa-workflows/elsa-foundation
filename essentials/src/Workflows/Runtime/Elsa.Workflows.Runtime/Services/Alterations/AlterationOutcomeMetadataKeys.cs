namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// The audit keys an operator alteration writes on the incident outcome it resolves (<c>RetryFaultedActivity/1</c> and
/// <c>DecideIntervention/1</c>): which job and plan applied it, who submitted the plan and why.
/// </summary>
internal static class AlterationOutcomeMetadataKeys
{
    public const string JobId = "runtime.alteration.jobId";
    public const string PlanId = "runtime.alteration.planId";
    public const string SubmittedBy = "runtime.alteration.submittedBy";
    public const string Reason = "runtime.alteration.reason";
}
