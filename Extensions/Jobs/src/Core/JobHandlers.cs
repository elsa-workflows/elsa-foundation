namespace Elsa.Jobs.Core;

/// <summary>Installed semantic support advertised by a worker; CLR registration types stay implementation-owned.</summary>
public sealed record JobHandlerSupport(
    JobHandlerIdentity Handler,
    JobSchema InputSchema,
    JobSchema ResultSchema,
    string ImplementationIdentity);

/// <summary>Invocation context supplied to a registered handler; stable idempotency identity survives retries.</summary>
public sealed record JobExecutionContext(
    JobScope Scope,
    string JobId,
    string AttemptId,
    int AttemptNumber,
    string IdempotencyKey,
    IJobProgressReporter Progress);

/// <summary>Bounded redacted informational progress; never authoritative completion.</summary>
public sealed record JobProgress(
    double? Fraction,
    string? Message,
    JobPayload? Data);

/// <summary>Replacement contract: ordinary producer submission; one implementation per configured Jobs authority.</summary>
public interface IJobClient
{
    ValueTask<JobAcceptance> SubmitAsync(JobScope scope, JobSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>Registered handler contribution for one explicit semantic identity; external effects require invocation idempotency.</summary>
public interface IJobHandler<TInput, TResult>
{
    ValueTask<TResult> ExecuteAsync(TInput input, JobExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Attempt-bound reporter supplied to a handler; implementations enforce current ownership and redaction.</summary>
public interface IJobProgressReporter
{
    ValueTask ReportAsync(JobProgress progress, CancellationToken cancellationToken = default);
}
