namespace Elsa.Workflows.Design.Provisioning.Core.Contracts;

public interface IWorkflowVersionProvisioner
{
    Task Provision(CancellationToken cancellationToken);
}
