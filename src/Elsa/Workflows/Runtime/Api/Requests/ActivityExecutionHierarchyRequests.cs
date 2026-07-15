using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecutionDescendants(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string? Cursor = null,
    int? Limit = null,
    string? Include = null) : IRequest<GetActivityExecutionDescendantsResponse>;

public sealed record GetActivityExecutionDescendantsResponse(ActivityExecutionHierarchyPageView? Page);

public sealed record GetActivityExecutionLayout(string WorkflowExecutionId, string ActivityExecutionId)
    : IRequest<GetActivityExecutionLayoutResponse>;

public sealed record GetActivityExecutionLayoutResponse(ActivityExecutionLayoutView? Layout);
