using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

public sealed record AddVersion(
    string? OperationKey,
    string DefinitionId,
    WorkflowDefinitionStateView State
)
: ICommand<WorkflowDefinitionVersionDetailsView>;
