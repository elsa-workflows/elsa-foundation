using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record AddVersion(
    string DefinitionId, 
    WorkflowDefinitionStateView State    
)
: ICommand<WorkflowDefinitionVersionDetailsView>;
