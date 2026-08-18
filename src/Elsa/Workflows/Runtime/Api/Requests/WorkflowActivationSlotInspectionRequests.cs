using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowActivationSlots(string DefinitionId) : IRequest<WorkflowActivationSlotListView>;

public sealed record GetWorkflowActivationSlot(string DefinitionId, string SlotName) : IRequest<WorkflowActivationSlotView>;
