namespace Elsa.Activities.Design.Provisioning.Core;

public interface IActivityDefinitionVersionProvisioner
{
    Task Provision(CancellationToken cancellationToken);
}
