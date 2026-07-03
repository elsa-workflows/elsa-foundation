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
