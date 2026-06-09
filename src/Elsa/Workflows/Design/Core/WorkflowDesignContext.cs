using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core;

public sealed class WorkflowDesignContext : IWorkflowDesignContext
{
    // Placeholder for the future DI-scoped design context. The intended model is similar to
    // WorkflowExecutionContext, but design endpoint/UI ownership has not defined who creates
    // and populates this scope yet.
    public WorkflowDefinitionState Draft => throw new NotImplementedException();

    public IEnumerable<IActivityDefinitionVersion> Activities => throw new NotImplementedException();
}
