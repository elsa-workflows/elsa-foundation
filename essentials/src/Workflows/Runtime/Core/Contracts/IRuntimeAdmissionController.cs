using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Decides whether one workflow execution command may run now or must be refused (RB1, #1235). Consulted by
/// <c>WorkflowSchedulerCommandRouter</c> before anything durable happens, which is what lets a refused start be
/// reported honestly as "we did not take this" rather than parked and reported as taken.
/// </summary>
public interface IRuntimeAdmissionController
{
    /// <summary>The limit the next decision will be made against, in dispatch units.</summary>
    double Limit { get; }

    /// <summary>
    /// Evaluates one command. An admitted decision holds an in-flight charge until it is disposed; disposal also
    /// feeds the completion latency back into the adaptive limit.
    /// </summary>
    RuntimeAdmissionDecision TryAdmit();
}
