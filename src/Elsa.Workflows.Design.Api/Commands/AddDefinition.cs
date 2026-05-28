using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record AddDefinition(
    string Name,
    string Description
)

: ICommand<WorkflowDefinitionVersionInfo>;
