namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Marks an exception an executor throws to wait for an expected condition, such as a bookmark not yet consumed,
/// rather than to report a failure. The outbox processor still retries it, but logs it without the exception so a
/// routine wait does not attach a stack trace to every retry.
/// </summary>
public interface IRuntimePostCommitDeferral;
