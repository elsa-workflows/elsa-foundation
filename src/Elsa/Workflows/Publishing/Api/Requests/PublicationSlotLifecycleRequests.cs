using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record UnpublishPublicationSlot(string WorkflowDefinitionId, string SlotName) : IRequest<PublicationSlot>;

public sealed record RestorePublicationSlot(string WorkflowDefinitionId, string SlotName) : IRequest<PublicationSlot>;
