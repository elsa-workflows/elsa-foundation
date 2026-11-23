namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// How an exception that faults an activity is classified: whether a later attempt may succeed, and a stable failure
/// code. The activity fault recorder reads both through this contract, never through a concrete exception type, so an
/// exception that wraps another can keep its classification by implementing it too.
/// </summary>
/// <remarks>
/// Recording <see cref="IsRetryable"/> on the fault does not retry anything: no runtime retry policy reads it yet.
/// An exception that does not implement this contract faults as not retryable, with its fault sub-status as the code.
/// </remarks>
public interface IRuntimeFaultClassification
{
    bool IsRetryable { get; }

    /// <summary>A stable code name for the failure, or <see langword="null"/> to keep the fault sub-status as the code.</summary>
    string? FailureCode { get; }
}
