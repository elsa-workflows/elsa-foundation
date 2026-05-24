using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Provisioning.Core;

public interface IActivityDefinitionVersionProvisioner
{
    Task Provision(IEnumerable<IActivityDefinitionVersionSource> sources, CancellationToken cancellationToken);
}
