using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Provisioning.Core;

public sealed record OnActivityVersionsProvisioning(ICollection<IActivityDefinitionVersion> Versions) : IDomainEvent;