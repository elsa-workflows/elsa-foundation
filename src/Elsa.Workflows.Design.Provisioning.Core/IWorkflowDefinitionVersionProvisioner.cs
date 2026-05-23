namespace Elsa.Workflows.Design.Provisioning.Core;

public interface IWorkflowDefinitionVersionProvisioner
{
    Task Provision(CancellationToken cancellationToken);
}
