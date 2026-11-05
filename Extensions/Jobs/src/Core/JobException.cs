namespace Elsa.Jobs.Core;

/// <summary>Domain boundary failure with a stable safe diagnostic code.</summary>
public class JobException : Exception
{
    public JobException(string code, string message) : base(message) => Code = code;

    public JobException(string code, string message, Exception innerException) : base(message, innerException) => Code = code;

    public string Code { get; }
}

/// <summary>Preserves a provider cause without requiring callers to reference the provider assembly.</summary>
public sealed class JobPersistenceException : JobException
{
    public JobPersistenceException(string operation, Exception innerException)
        : base("jobs.persistence.unavailable", $"Jobs persistence operation '{operation}' failed.", innerException) => Operation = operation;

    public string Operation { get; }
}

/// <summary>A protected submission identity already recognizes different immutable content.</summary>
public sealed class JobSubmissionConflictException : JobException
{
    public JobSubmissionConflictException(string idempotencyKey)
        : base("jobs.submission.conflict", "The submission identity is already associated with different job content.") => IdempotencyKey = idempotencyKey;

    public string IdempotencyKey { get; }
}
