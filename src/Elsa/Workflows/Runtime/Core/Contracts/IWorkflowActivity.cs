using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IWorkflowActivity : IActivity
{
    IDictionary<string, InputArgument> Inputs { get; }
}
