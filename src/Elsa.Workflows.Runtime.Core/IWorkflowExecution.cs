using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core
{
    public interface IWorkflowExecution
    {
        Thread CurrentThread { get; }        

        IWorkflowExecutionContext ExecutionContext { get; }

        string DefinitionId { get; }

        string VersionId { get; }

        int Version { get; }

        IEnumerable<IVariable> Variables { get; }
    }
}
