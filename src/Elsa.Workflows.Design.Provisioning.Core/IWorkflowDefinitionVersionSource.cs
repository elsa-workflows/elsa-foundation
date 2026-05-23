using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Provisioning.Core;

public interface IWorkflowDefinitionVersionSource
{
    ValueTask<IEnumerable<IWorkflowDefinitionVersion>> Get(CancellationToken cancellationToken);
}
