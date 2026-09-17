using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed class PersistenceWorkflowExecutionPartitionAccessor(
    IPersistenceAccessContextAccessor persistenceAccessContextAccessor) : IWorkflowExecutionPartitionAccessor
{
    public WorkflowExecutionPartition Current => new(persistenceAccessContextAccessor.Current.RequireScope().Value);
}
