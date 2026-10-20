using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Projects a live compensation scope's private state into the read-only journal operators read (spec 260 FR-414, view
/// <c>compensation.journal</c> version 1): phase, cause, every entry with its forward and handler occurrences, attempts
/// and incidents, the wait and the decisions it offers, resolutions, origins and confirmations, the entries still to
/// undo in recovery order, the hand-over progress and the abandonment. It carries no captured value; the cause message
/// is included only for a caller who may inspect sensitive values.
/// </summary>
internal sealed class CompensationJournalProjector : IActivityStateViewProjector
{
    public const string Kind = "compensation.journal";
    public const int Version = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string StateTypeAlias => CompensationScopeStatePersister.StateTypeAlias;
    public string ViewKind => Kind;
    public int ViewVersion => Version;

    public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = CompensationScopeStatePersister.Load(context.ActivityExecution)
                    ?? throw new InvalidOperationException("A compensation journal needs the scope's private state.");
        var navigator = context.ExecutableNode.GetOrAddRoutingStructure(CompensationScopeNavigator.From);
        var incidents = context.Incidents.ToDictionary(incident => incident.IncidentId, StringComparer.Ordinal);
        var canRecover = state.Phase is CompensationScopePhase.Running or CompensationScopePhase.Compensating or CompensationScopePhase.AwaitingIntervention;

        var view = new JournalView(
            state.Phase.ToString(),
            CompensationScopeStatePersister.StateSchemaVersion,
            Cause(state.Cause, incidents, context.CanInspectSensitiveValues),
            state.Entries.OrderBy(entry => entry.Ordinal).Select(entry => Entry(entry, state, navigator, incidents, canRecover)).ToArray(),
            canRecover
                ? state.Entries
                    .Where(entry => IsStillToUndo(entry, navigator))
                    .OrderByDescending(entry => entry.Ordinal)
                    .Select(entry => entry.Id)
                    .ToArray()
                : [],
            state.Confirmations.Select(confirmation => new ConfirmationView(confirmation.ActivityExecutionId, confirmation.EntryIds)).ToArray(),
            new HandOversView(
                state.ReceivedHandOvers,
                state.HandOversInProgress.Select(progress => new HandOverProgressView(progress.OriginScope, progress.Batches)).ToArray()),
            state.Abandonment is { } abandonment
                ? new AbandonmentView(abandonment.IncidentId, abandonment.EntryId, abandonment.FromPhase.ToString())
                : null);
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(view, SerializerOptions));
    }

    // Undone yet to come: an entry whose handler has not completed and that recovery still runs, newest first.
    private static bool IsStillToUndo(CompensationEntry entry, CompensationScopeNavigator navigator) =>
        entry.Status is CompensationEntryStatus.Registered or CompensationEntryStatus.Uncertain or CompensationEntryStatus.Compensating or CompensationEntryStatus.Failed &&
        CompensationScope.HandlerNodeOf(navigator, entry) is not null;

    private static CauseView? Cause(CompensationRecoveryCause? cause, IReadOnlyDictionary<string, IncidentState> incidents, bool canInspectValues)
    {
        if (cause is null)
            return null;

        var incident = cause.IncidentId is { } incidentId ? incidents.GetValueOrDefault(incidentId) : null;
        return new CauseView(
            cause.Kind,
            cause.Step,
            cause.ActivityExecutionId,
            cause.IncidentId,
            incident?.FailureType,
            canInspectValues ? incident?.Message : null,
            !canInspectValues && incident is not null,
            cause.RequestedBy,
            cause.Target);
    }

    private static EntryView Entry(
        CompensationEntry entry,
        CompensationScopeState state,
        CompensationScopeNavigator navigator,
        IReadOnlyDictionary<string, IncidentState> incidents,
        bool canRecover)
    {
        var handlerNodeId = CompensationScope.HandlerNodeOf(navigator, entry);
        var attempts = entry.HandlerAttempts
            .Select((attempt, index) => new AttemptView(index + 1, attempt.ActivityExecutionId, attempt.IncidentId, attempt.Result.ToString()))
            .ToArray();
        var waiting = entry.InterventionIncidentId is { } waitingIncidentId
            ? new WaitingView(waitingIncidentId, OfferedDecisions(incidents.GetValueOrDefault(waitingIncidentId)))
            : null;
        var confirmedBy = state.Confirmations.FirstOrDefault(confirmation => confirmation.EntryIds.Contains(entry.Id, StringComparer.Ordinal))?.ActivityExecutionId;
        return new EntryView(
            entry.Id,
            entry.Step,
            entry.Ordinal,
            entry.Status.ToString(),
            entry.ForwardOutcome.ToString(),
            entry.OperationKey,
            new OccurrenceView(entry.ForwardActivityExecutionId, entry.ForwardIterationId),
            handlerNodeId is null ? null : new HandlerView(handlerNodeId, canRecover && IsStillToUndo(entry, navigator)),
            attempts,
            waiting,
            entry.Resolution is { } resolution ? new ResolutionView(resolution.Outcome.ToString(), resolution.IncidentId) : null,
            entry.Origin is { } origin ? new OriginView(origin.ScopeActivityExecutionId, origin.EntryId) : null,
            confirmedBy);
    }

    // The decisions the wait's outcome offers; a wait from before spec 260 offers none.
    private static IReadOnlyList<string> OfferedDecisions(IncidentState? incident)
    {
        if (incident?.ResolutionOutcome?.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InterventionDecisions) is not { } raw)
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record JournalView(
        string Phase,
        int StateVersion,
        CauseView? Cause,
        IReadOnlyList<EntryView> Entries,
        IReadOnlyList<string> Remaining,
        IReadOnlyList<ConfirmationView> Confirmations,
        HandOversView HandOvers,
        AbandonmentView? Abandonment);

    private sealed record CauseView(
        string Kind,
        string Step,
        string ActivityExecutionId,
        string? IncidentId,
        string? FailureType,
        string? Message,
        bool MessageWithheld,
        string? RequestedBy,
        string? Target);

    private sealed record EntryView(
        string Id,
        string Step,
        int Ordinal,
        string Status,
        string ForwardOutcome,
        string OperationKey,
        OccurrenceView Forward,
        HandlerView? Handler,
        IReadOnlyList<AttemptView> Attempts,
        WaitingView? Waiting,
        ResolutionView? Resolution,
        OriginView? Origin,
        string? ConfirmedBy);

    private sealed record OccurrenceView(string ActivityExecutionId, string? IterationId);

    private sealed record HandlerView(string ExecutableNodeId, bool Remaining);

    private sealed record AttemptView(int Number, string? ActivityExecutionId, string? IncidentId, string Result);

    private sealed record WaitingView(string IncidentId, IReadOnlyList<string> Decisions);

    private sealed record ResolutionView(string Outcome, string IncidentId);

    private sealed record OriginView(string ScopeActivityExecutionId, string EntryId);

    private sealed record ConfirmationView(string ActivityExecutionId, IReadOnlyList<string> EntryIds);

    private sealed record HandOversView(IReadOnlyList<string> Received, IReadOnlyList<HandOverProgressView> InProgress);

    private sealed record HandOverProgressView(string OriginScope, IReadOnlyList<int> Batches);

    private sealed record AbandonmentView(string IncidentId, string EntryId, string FromPhase);
}
