using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The single runtime fault-capture policy: turns an <see cref="Exception"/> into a structured
/// <see cref="RuntimeFaultInfo"/>. One implementation is active per runtime composition so the scheduler drainer
/// and the post-commit outbox capture faults the same way instead of each rolling its own convention (RT-12).
/// </summary>
public interface IRuntimeFaultCapturePolicy
{
    RuntimeFaultInfo Capture(Exception exception);
}

public static class RuntimeFaultCapturePolicyExtensions
{
    /// <summary>
    /// Captures the exception's first inner exception under the same policy (same scrubbing/stack-trace rules as
    /// the outer capture), or <c>null</c> when there is none. Runtime faults are routinely wrapped one level deep
    /// (e.g. a checkpoint-writer exception around the physical storage fault), so surfacing the first inner fault
    /// alongside the outer one keeps the root cause diagnosable (#1031).
    /// </summary>
    public static RuntimeFaultInfo? CaptureInner(this IRuntimeFaultCapturePolicy policy, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is { } inner ? policy.Capture(inner) : null;
    }
}
