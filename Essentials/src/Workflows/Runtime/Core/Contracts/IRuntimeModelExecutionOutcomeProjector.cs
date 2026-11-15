using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Pure model-owned interpretation of an already authorized retained terminal result. It starts no runtime services.</summary>
public interface IRuntimeModelExecutionOutcomeProjector
{
    string ModelKey { get; }
    /// <summary>Returns Completed, Faulted or Cancelled for a recognized terminal result; otherwise null.</summary>
    WorkflowExecutionStatus? Project(RuntimeModelExecutionState execution);
}
