using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record ListDefinitions(
    string? Id,    
    string? Name,
    string? SearchTerm,
    string? Description,
    bool? IsSystem,
    string? MaterializerName,
    bool? TenantAgnostic
)

: IRequest<IEnumerable<WorkflowDefinitionView>>;
