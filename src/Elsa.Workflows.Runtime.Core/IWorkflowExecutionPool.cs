namespace Elsa.Workflows.Runtime.Core
{
    public interface IWorkflowExecutionPool
    {
        ValueTask<IWorkflowExecutionContext> StartWorkflowExecution();

        ValueTask<IWorkflowExecutionContext> GetWorkflowExecution(string workflowExecutionId);
    }
}