using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Provisioning.Core.Events;

public sealed record OnWorkflowVersionsProvisioning(ICollection<IWorkflowDefinitionVersion> Versions) : IDomainEvent;