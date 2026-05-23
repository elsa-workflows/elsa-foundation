using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts
{
    public interface IWorkflowExecutionContext
    {
        string WorkflowDefinitionId { get; }

        string WorkflowDefinitionVersionId { get; }

        int WorkflowDefinitionVersion { get; }

        void SetVariable(string variableName, object? value);

        object GetVariable(string variableName);

        object GetInput(string inputName);

        object GetOutput(string outputName);

        object GetOutput(string outputName, string activityIdOrName);

        object? GetLastActivityResult();

        string InstanceId { get; }

        string? CorrelationId { get; set; }

        string? Name { get; set; }

        IEnumerable<IVariable> GetVariables();

        IDictionary<string, InputArgument> GetWorkflowInputs();

        IActivityExecutionContext GetActivityContextForExpression(IExpressionExecutionContext expressionContext);
    }
}
