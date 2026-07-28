using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record AddDefinition(
    string? OperationKey,
    string Name,
    string? Description,
    WorkflowDefinitionStateView? InitialState = null,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null)

: ICommand<WorkflowDefinitionDetailsView>;
