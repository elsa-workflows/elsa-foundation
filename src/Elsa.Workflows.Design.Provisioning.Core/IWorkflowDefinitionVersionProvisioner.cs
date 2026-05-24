namespace Elsa.Workflows.Design.Provisioning.Core;

public interface IWorkflowDefinitionVersionProvisioner
{
    Task Provision(IEnumerable<IWorkflowDefinitionVersionSource> sources, CancellationToken cancellationToken);
}
