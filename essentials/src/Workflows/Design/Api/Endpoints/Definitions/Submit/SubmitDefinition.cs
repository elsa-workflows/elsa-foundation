using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;

public sealed record SubmitDefinition(
    string? OperationKey,
    string Name,
    string? Description,
    WorkflowDefinitionStateView State)
    : ICommand<SubmittedWorkflowDefinitionView>;
