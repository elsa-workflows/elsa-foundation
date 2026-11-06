namespace Elsa.Jobs.Core;

/// <summary>Optional scoped data-boundary contribution. Admission is pure and never resolves protected values.</summary>
public interface IJobDataPolicy
{
    void ValidateAdmission(JobScope scope, JobRequest request);
    void BeginInvocation(JobExecutionContext context, JobRequest request);
    void ValidateProgress(JobExecutionContext context, JobProgress progress);
    void ValidateResult(JobExecutionContext context, JobPayload? payload);
}

/// <summary>Fixed nonretryable policy refusal; carries no submitted value, provider text or inner exception.</summary>
public sealed class JobDataPolicyException() : JobException("jobs.data-policy.refused", "The Jobs data policy refused this operation.");

/// <summary>Fixed transient invocation failure; retries use the accepted job policy and carry no provider text or inner exception.</summary>
public sealed class JobDataUnavailableException() : JobException("jobs.data-policy.unavailable", "The Jobs data policy is temporarily unavailable.");
