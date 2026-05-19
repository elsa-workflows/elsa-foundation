using Elsa.Workflows.Design.Core;

namespace Elsa.Activities.Design.Core.Contracts
{
    public interface IActivityNode
    {
        IActivityDefinition Definition { get; }

        IEnumerable<IActivityConnection> Connections { get; }
    }
}
