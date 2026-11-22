using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Thrown when an evaluation staged a delayed child schedule the runtime refuses (R-7, spec 261). The evaluation faults
/// with <see cref="FailureCode"/>, one of <c>RuntimeDelayedScheduleFaultCodes</c>, and commits none of what it staged.
/// </summary>
public sealed class RuntimeDelayedScheduleException(string failureCode, string message) : InvalidOperationException(message), IRuntimeFaultClassification
{
    public string FailureCode { get; } = failureCode;

    public bool IsRetryable => false;
}
