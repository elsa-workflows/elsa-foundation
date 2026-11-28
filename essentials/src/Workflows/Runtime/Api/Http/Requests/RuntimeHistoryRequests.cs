using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetRuntimeHistory(string WorkflowExecutionId, int Limit = 100, string? Cursor = null,
    string? ActivityExecutionId = null, bool IncludeDescendants = false, string? BookmarkId = null,
    RuntimeHistoryEventCategory? Category = null, string? Name = null, string? Search = null);
