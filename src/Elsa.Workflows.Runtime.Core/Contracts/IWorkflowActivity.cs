using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IWorkflowActivity : IActivity
{
    IDictionary<string, InputArgument> Inputs { get; }
}
