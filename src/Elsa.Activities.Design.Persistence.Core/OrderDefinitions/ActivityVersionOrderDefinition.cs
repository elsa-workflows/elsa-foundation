using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Persistence;
namespace Elsa.Activities.Design.Persistence.Core.OrderDefinitions;

public sealed class ActivityVersionOrderDefinition(OrderDirection versionOrderDirection = OrderDirection.Descending) : OrderDefinition<ActivityDefinitionVersion, int>((v) => v.Version, versionOrderDirection)
{
}
