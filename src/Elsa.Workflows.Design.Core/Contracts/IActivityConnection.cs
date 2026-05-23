using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts
{
    public interface IActivityConnection
    {
        ActivityPortConnection Source { get; }

        ActivityPortConnection Target { get; }
    }
}
