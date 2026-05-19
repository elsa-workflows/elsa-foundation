using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Design.Core
{
    public interface IWorkflowGraph
    {
        IVariable[] Variables { get; }

        IEnumerable<IActivityNode> ActivityNodes { get; }

        IEnumerable<IInputDefinition> Inputs { get; }
    }
}
