using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetWorkflowInstance(
    string WorkflowExecutionId,
    int ActivityPageSize = RuntimeStorePageRequest.DefaultLimit,
    string? ActivityContinuationToken = null);
