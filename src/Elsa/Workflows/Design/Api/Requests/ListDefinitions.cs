using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record ListDefinitions(
    string? Id,
    string? Name,
    string? SearchTerm,
    string? Description,
    string? State = null,
    int Page = 1,
    int PageSize = 50,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyCollection<string>? MarkerTagClauses = null,
    IReadOnlyCollection<string>? ControlledTagClauses = null,
    IReadOnlyCollection<string>? ControlledTagFacets = null,
    string? GroupByControlledTagDefinitionId = null
)

: IRequest<WorkflowDefinitionListView>;
