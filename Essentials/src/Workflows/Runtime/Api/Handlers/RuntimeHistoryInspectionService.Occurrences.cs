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
        var binding = OccurrenceAccessBinding is { } bind
            ? await bind(workflow, requestBinding, token) : null;
        if (binding is null) return EmptyOccurrences("current-authorization-unsupported", await CoverageAsync(workflow, null, token));
        query = new(request.WorkflowExecutionId, request.Limit, request.SequenceToken, request.Position,
            request.AnchorActivityExecutionId, request.ActivityExecutionId, request.IncludeDescendants, binding);
        try
        {
            var page = await occurrences.ReadOccurrencesAsync(query, token);
            ValidateOccurrences(query, page);
            // A pending provider read must not disclose its result after access/profile retirement.
            workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
            var currentBinding = await OccurrenceAccessBinding!(workflow, await BindingAsync(token), token);
            if (currentBinding != query.AccessBinding) throw new RuntimeResourceMissingSignal();
            var coverage = await CoverageAsync(workflow, page.Head, token);
            var state = page.Availability switch
            {
                RuntimeHistoryOccurrenceAvailability.Available => coverage.State == "partial" ? "partial" : "available",
                RuntimeHistoryOccurrenceAvailability.Partial => "partial",
                RuntimeHistoryOccurrenceAvailability.Invalidated => "range-invalidated",
                RuntimeHistoryOccurrenceAvailability.TargetUnavailable => "target-unavailable",
                _ => "unavailable"
            };
            return new(state, SafeOccurrenceReason(page.Reason), RuntimeHistoryOccurrenceCoverageView.From(coverage),
                RuntimeHistoryOccurrenceCoverageView.Decimal(page.UpperSequence),
                RuntimeHistoryOccurrenceCoverageView.Decimal(page.RetainedAfterSequence),
                page.TotalCount is { } count ? RuntimeHistoryOccurrenceCoverageView.Decimal(count) : null,
                page.SelectedPosition is { } position ? RuntimeHistoryOccurrenceCoverageView.Decimal(position) : null,
                page.Items.Select(RuntimeHistoryOccurrenceView.From).ToArray(), page.SequenceToken,
                page.HasNewerHistory, time.GetUtcNow());
        }
        catch (RuntimeHistoryIntegrityException)
        {
            return EmptyOccurrences("history-invalid", InvalidCoverage());
        }
    }

    private RuntimeHistoryOccurrencePageView EmptyOccurrences(string reason, RuntimeHistoryCoverageView coverage) =>
        new("unavailable", reason, RuntimeHistoryOccurrenceCoverageView.From(coverage), "0", "0", null, null, [], null, false, time.GetUtcNow());

    private static string? SafeOccurrenceReason(string? reason) => reason switch
    {
        null => null,
        "retained-range-changed" or "history-unavailable" or "history-gap-unlocated" or
        "occurrence-recording-unavailable" or "occurrence-observation-unknown" or "scope-unavailable" or
        "scope-ancestry-unknown" or "anchor-outside-range" or "anchor-outside-scope" or
        "position-outside-range" or "retained-prefix-purged" or "capture-not-from-start" => reason,
        _ => "occurrence-limitation-unknown"
    };

    private static void ValidateOccurrences(RuntimeHistoryOccurrenceQuery query, RuntimeHistoryOccurrencePage page)
    {
        void Invalid() => throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId,
            "occurrence page disagrees with its authorized committed window");
        if (!Enum.IsDefined(page.Availability) || page.Items.Count > query.Limit || page.UpperSequence < 0 ||
            page.RetainedAfterSequence < 0 || page.TotalCount < 0 || page.SelectedPosition < 0 ||
            page.Head is { } owned && owned.WorkflowExecutionId != query.WorkflowExecutionId) Invalid();
        var playable = page.Availability is RuntimeHistoryOccurrenceAvailability.Available or RuntimeHistoryOccurrenceAvailability.Partial;
        if (!playable)
        {
            if (page.Items.Count != 0 || page.SequenceToken is not null || page.TotalCount is not null || page.SelectedPosition is not null) Invalid();
            return;
        }
        if (page.Head is not { } head || head.HasGaps || head.LastSequence < page.UpperSequence ||
            head.PurgedThroughSequence != page.RetainedAfterSequence || page.RetainedAfterSequence > page.UpperSequence ||
            page.HasNewerHistory != (head.LastSequence > page.UpperSequence) || string.IsNullOrWhiteSpace(page.SequenceToken) ||
            query.SequenceToken is not null && query.SequenceToken != page.SequenceToken ||
            page.TotalCount == 0 && (page.Items.Count != 0 || page.SelectedPosition is not null) ||
            page.Items.Count == 0 && (page.SelectedPosition is not null || page.TotalCount != 0 ||
                query.Position is not null || query.AnchorActivityExecutionId is not null) ||
            page.TotalCount is > 0 && page.Items.Count == 0) Invalid();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long previousSequence = page.RetainedAfterSequence;
        long? previousPosition = null;
        RuntimeHistoryOccurrence? previousEntry = null;
        foreach (var entry in page.Items)
        {
            var item = entry.Observation;
            EnsureEventOwner(query.WorkflowExecutionId, item);
            EnsureEventPosition(item, page.Head);
            if (!RuntimeHistoryOccurrence.IsEligible(item) || item.Sequence <= previousSequence || item.Sequence > page.UpperSequence ||
                entry.Position < 0 || page.TotalCount is { } count && entry.Position >= count ||
                previousPosition is { } prior && entry.Position != prior + 1 || !ids.Add(item.ActivityExecutionId!) ||
                query.ActivityExecutionId is { } scope && !query.IncludeDescendants && item.ActivityExecutionId != scope ||
                entry.Position == 0 && entry.PreviousActivityExecutionId is not null ||
                page.TotalCount is { } total && entry.Position == total - 1 && entry.NextActivityExecutionId is not null ||
                previousEntry is { } neighbor && (entry.PreviousActivityExecutionId != neighbor.Observation.ActivityExecutionId ||
                    neighbor.NextActivityExecutionId != item.ActivityExecutionId)) Invalid();
            previousSequence = item.Sequence; previousPosition = entry.Position;
            previousEntry = entry;
        }
        if (page.Items.Count > 0 && (page.SelectedPosition is not { } selected ||
            !page.Items.Any(entry => entry.Position == selected &&
                (query.AnchorActivityExecutionId is null || entry.Observation.ActivityExecutionId == query.AnchorActivityExecutionId)) ||
            query.Position is { } requested && requested != selected)) Invalid();
    }
}
