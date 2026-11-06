using Elsa.Jobs.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Elsa.Jobs;

internal sealed class JobClient(IServiceProvider services, JobRuntimeSettings settings) : IJobClient
{
    public async ValueTask<JobAcceptance> SubmitAsync(JobScope scope, JobSubmission submission, CancellationToken cancellationToken = default)
    {
        JobValidation.Scope(scope);
        ArgumentNullException.ThrowIfNull(submission);
        JobValidation.Identity(submission.IdempotencyKey);
        JobValidation.Request(submission.Request, settings.MaximumPayloadBytes);
        if (submission.Request.Association is not null)
            throw new JobException("jobs.association.untrusted", "Ordinary job submission cannot supply a trusted workflow association.");
        var request = submission.Request;
        foreach (var policy in services.GetServices<IJobDataPolicy>()) policy.ValidateAdmission(scope, request);
        var admission = new JobAdmission(scope, Guid.NewGuid().ToString("N"), submission.IdempotencyKey,
            JobRequestDigest.Compute(request), request, null);
        var result = await JobAuthority.Commands(services).AdmitAsync(admission, cancellationToken);
        if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed && result.Acceptance is { } accepted)
            return accepted;
        if (result.Status == JobCommandStatus.Conflict) throw new JobSubmissionConflictException(submission.IdempotencyKey);
        throw new JobException("jobs.submission.unavailable", "The Jobs authority did not confirm durable acceptance; retry only with the original protected identity.");
    }
}
