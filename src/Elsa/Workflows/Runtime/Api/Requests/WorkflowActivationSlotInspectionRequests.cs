namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowActivationSlots(string DefinitionId);

public sealed record GetWorkflowActivationSlot(string DefinitionId, string SlotName);
