namespace Elsa.Workflows.Runtime.Core.Contracts
{
    public interface IWorkflowExecutionPool
    {
        ValueTask<IWorkflowExecutionContext> StartWorkflowExecution();

        ValueTask<IWorkflowExecutionContext> GetWorkflowExecution(string workflowExecutionId);
    }
}