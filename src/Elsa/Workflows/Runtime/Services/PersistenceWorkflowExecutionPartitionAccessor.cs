using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

internal sealed class PersistenceWorkflowExecutionPartitionAccessor(
    IPersistenceAccessContextAccessor persistenceAccessContextAccessor) : IWorkflowExecutionPartitionAccessor
{
    public WorkflowExecutionPartition Current
    {
        get
        {
            var scope = persistenceAccessContextAccessor.Current.Scope
                ?? throw new InvalidOperationException(
                    "Workflow runtime operations require a tenant-scoped persistence access context.");
            return new WorkflowExecutionPartition(scope.Value);
        }
    }
}
