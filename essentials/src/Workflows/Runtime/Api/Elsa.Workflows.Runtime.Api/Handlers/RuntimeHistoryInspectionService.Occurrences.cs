using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed partial class RuntimeHistoryInspectionService
{
    internal Func<WorkflowExecutionState, string, CancellationToken, ValueTask<string?>>? OccurrenceAccessBinding { get; init; }

    public async Task<RuntimeHistoryOccurrencePageView> ReadOccurrencesAsync(GetRuntimeHistoryOccurrences request,
        CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        var requestBinding = await BindingAsync(token);
        var query = new RuntimeHistoryOccurrenceQuery(request.WorkflowExecutionId, request.Limit, request.SequenceToken,
            request.Position, request.AnchorActivityExecutionId, request.ActivityExecutionId,
            request.IncludeDescendants, requestBinding);
        if (history is not IRuntimeHistoryOccurrenceStore occurrences)
            return EmptyOccurrences("occurrence-facet-unsupported", await CoverageAsync(workflow, null, token));
        var binding = await BindOccurrencesAsync(workflow, requestBinding, token);
        if (binding is null) return EmptyOccurrences("current-authorization-unsupported", await CoverageAsync(workflow, null, token));
        query = new(request.WorkflowExecutionId, request.Limit, request.SequenceToken, request.Position,
            request.AnchorActivityExecutionId, request.ActivityExecutionId, request.IncludeDescendants, binding);
        try
        {
            var page = await occurrences.ReadOccurrencesAsync(query, token);
            ValidateOccurrences(query, page);
            var coverage = await CoverageAsync(workflow, page.Head, token);
            // Coverage also awaits native-source reads; recheck after every read before disclosure.
            workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
            var currentBinding = await BindOccurrencesAsync(workflow, await BindingAsync(token), token);
            if (currentBinding != query.AccessBinding) throw new RuntimeResourceMissingSignal();
            return PresentOccurrences(page, coverage, time.GetUtcNow());
        }
        catch (RuntimeHistoryIntegrityException)
        {
            workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
            var currentBinding = await BindOccurrencesAsync(workflow, await BindingAsync(token), token);
            if (currentBinding != query.AccessBinding) throw new RuntimeResourceMissingSignal();
            return EmptyOccurrences("history-invalid", InvalidCoverage());
        }
    }

    internal ValueTask<string?> BindOccurrencesAsync(WorkflowExecutionState workflow, string requestBinding, CancellationToken token) =>
        OccurrenceAccessBinding?.Invoke(workflow, requestBinding, token) ?? ValueTask.FromResult<string?>(null);

    ValueTask<string?> ILegacyRuntimeHistoryReader.BindOccurrencesAsync(WorkflowExecutionState state, string requestBinding,
        CancellationToken token) => BindOccurrencesAsync(state, requestBinding, token);

    internal static RuntimeHistoryOccurrencePageView PresentOccurrences(RuntimeHistoryOccurrencePage page,
        RuntimeHistoryCoverageView coverage, DateTimeOffset readAt) =>
        RuntimeHistoryOccurrenceProjection.PresentOccurrences(page, coverage, readAt);

    internal static void ValidateOccurrences(RuntimeHistoryOccurrenceQuery query, RuntimeHistoryOccurrencePage page) =>
        RuntimeHistoryOccurrenceProjection.ValidateOccurrences(query, page);

    private RuntimeHistoryOccurrencePageView EmptyOccurrences(string reason, RuntimeHistoryCoverageView coverage) =>
        new("unavailable", reason, RuntimeHistoryOccurrenceCoverageView.From(coverage), "0", "0", null, null, [], null, false, time.GetUtcNow());
}
