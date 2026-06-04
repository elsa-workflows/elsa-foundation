using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core;

public sealed class WorkflowExecutionContext : IWorkflowExecutionContext
{
    public string WorkflowDefinitionId => throw new NotImplementedException();

    public string WorkflowDefinitionVersionId => throw new NotImplementedException();

    public int WorkflowDefinitionVersion => throw new NotImplementedException();

    public string InstanceId => throw new NotImplementedException();

    public string? CorrelationId { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string? Name { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public IActivityExecutionContext GetActivityContextForExpression(IExpressionExecutionContext expressionContext)
    {
        throw new NotImplementedException();
    }

    public object GetInput(string inputName)
    {
        throw new NotImplementedException();
    }

    public object? GetLastActivityResult()
    {
        throw new NotImplementedException();
    }

    public object GetOutput(string outputName)
    {
        throw new NotImplementedException();
    }

    public object GetOutput(string outputName, string activityIdOrName)
    {
        throw new NotImplementedException();
    }

    public object GetVariable(string variableName)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<IVariable> GetVariables()
    {
        throw new NotImplementedException();
    }

    public IDictionary<string, InputArgument> GetWorkflowInputs()
    {
        throw new NotImplementedException();
    }

    public void SetVariable(string variableName, object? value)
    {
        throw new NotImplementedException();
    }
}
