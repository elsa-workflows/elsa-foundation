namespace Elsa.Workflows.Runtime.Core
{
    public interface IWorkflowExecutionContext
    {
        void SetVariable(string variableName, object? value);

        object GetVariable(string variableName);

        object GetInput(string inputName);

        object GetOutput(string outputName);

        object GetOutput(string outputName, string activityIdOrName);

        object? GetLastActivityResult();

        string InstanceId { get; }

        string? CorrelationId { get; set; }

        string? Name { get; set; }

        IEnumerable<WorkflowInput> GetWorkflowInputs();
    }
}
