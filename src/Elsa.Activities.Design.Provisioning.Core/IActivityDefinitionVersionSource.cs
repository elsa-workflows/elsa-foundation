using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Provisioning.Core;

public interface IActivityDefinitionVersionSource
{
    ValueTask<IEnumerable<IActivityDefinitionVersion>> Get(CancellationToken cancellationToken);
}
