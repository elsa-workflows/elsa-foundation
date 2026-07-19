using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record ListWorkflowDefinitionPage(
    int? PageSize = null,
    string? ContinuationToken = null,
    string? Search = null,
    string? State = null)
    : IRequest<WorkflowDefinitionPageView>;
