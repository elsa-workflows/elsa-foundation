using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core
{
    public interface IActivityNode
    {
        IActivityDefinition Definition { get; }

        IEnumerable<IActivityConnection> Connections { get; }
    }
}
