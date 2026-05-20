using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core
{
    public interface IActivityConnection
    {
        IActivityDefinition Incoming { get; }
        IActivityDefinition Outgoing { get; }
    }
}
