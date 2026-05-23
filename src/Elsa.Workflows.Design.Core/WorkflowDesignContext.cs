using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core;

public sealed class WorkflowDesignContext : IWorkflowDesignContext
{
    public IWorkflowDefinitionState Draft => throw new NotImplementedException();

    public IEnumerable<IActivityDefinitionVersion> Activities => throw new NotImplementedException();
}
