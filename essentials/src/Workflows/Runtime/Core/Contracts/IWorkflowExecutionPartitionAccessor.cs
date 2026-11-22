using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provides the immutable provider-neutral partition selected for the current runtime operation.</summary>
public interface IWorkflowExecutionPartitionAccessor
{
    WorkflowExecutionPartition Current { get; }
}
