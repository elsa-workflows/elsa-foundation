namespace Elsa.Activities.Design.Provisioning.Core;

public interface IActivityVersionProvisioner
{
    Task Provision(CancellationToken cancellationToken);
}
