using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;

using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>Access to the classic readers by concrete registration, even when the hybrid replaces the public readers.</summary>
internal interface ILegacyRuntimePreviewReaders
{
    ILegacyRunDetailsReader? Details { get; }
    ILegacyRuntimeHistoryReader? History { get; }
    IWorkflowInstanceDeletionService? Deletion { get; }
}

internal interface ILegacyRunDetailsReader
{
    Task<WorkflowInstanceDetailsView?> GetAsync(WorkflowExecutionState state, GetWorkflowInstance request,
        CancellationToken token);
}

internal interface ILegacyRuntimeHistoryReader
{
    Task<RuntimeHistoryPageView> ReadAsync(GetRuntimeHistory request, CancellationToken token);
    Task<RuntimeHistoryOccurrencePageView> ReadOccurrencesAsync(GetRuntimeHistoryOccurrences request, CancellationToken token);
    ValueTask<string?> BindOccurrencesAsync(WorkflowExecutionState state, string requestBinding, CancellationToken token);
}
