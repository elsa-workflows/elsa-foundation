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
    IJobProgressReporter Progress)
{
    /// <summary>Accepted logical handler identity supplied by the worker; not a caller-selected tenant or CLR type.</summary>
    public JobHandlerIdentity? Handler { get; init; }
}

/// <summary>Bounded redacted informational progress; never authoritative completion.</summary>
public sealed record JobProgress(
    double? Fraction,
    string? Message,
    JobPayload? Data);

/// <summary>Replacement contract: ordinary producer submission; one implementation per configured Jobs authority.</summary>
public interface IJobClient
{
    /// <summary>Submits one validated immutable invocation and returns only confirmed durable acceptance.</summary>
    /// <exception cref="JobException">The request is invalid, untrusted or unconfirmed by its authority; serialization/provider failures retain their original cause as <see cref="Exception.InnerException"/>.</exception>
    /// <exception cref="JobSubmissionConflictException">The protected producer key already recognizes different immutable content.</exception>
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
    /// <summary>Reports one bounded safe observation while the exact physical attempt owns progress authority.</summary>
    /// <exception cref="JobException">The report is invalid, unavailable or no longer owned; serialization/provider failures retain their original cause as <see cref="Exception.InnerException"/>.</exception>
    ValueTask ReportAsync(JobProgress progress, CancellationToken cancellationToken = default);
}
