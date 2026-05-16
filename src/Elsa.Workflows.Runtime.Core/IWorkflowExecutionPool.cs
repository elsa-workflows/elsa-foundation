namespace Elsa.Workflows.Runtime.Core
{
    public interface IWorkflowExecutionPool
    {
        ValueTask<IWorkflowExecution> StartWorkflowExecution();

        ValueTask<IWorkflowExecution> GetWorkflowExecution(string workflowExecutionId);
    }
}