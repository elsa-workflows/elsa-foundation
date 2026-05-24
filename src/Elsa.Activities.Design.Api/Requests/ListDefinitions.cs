using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record ListDefinitions(
    string? Id,    
    string? Category,
    string? SearchTerm,
    string? DisplayName,
    string? Description,
    bool? IsBrowsable,
    bool? TenantAgnostic
)

: IRequest<IEnumerable<ActivityDefinitionView>>;
