using System.Text.Json.Serialization;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>The scope's phase. Persisted by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompensationScopePhase>))]
internal enum CompensationScopePhase
{
    Running,
    Compensating,
    Completed,
    Compensated
}

/// <summary>The recovery status of one journal entry. Persisted by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompensationEntryStatus>))]
internal enum CompensationEntryStatus
{
    /// <summary>The forward occurrence completed and its step has a handler.</summary>
    Registered,

    /// <summary>The forward occurrence completed and its step has no handler.</summary>
    NoHandler,

    /// <summary>The handler's schedule request was committed together with this status.</summary>
    Compensating,

    /// <summary>The handler completed.</summary>
    Compensated
}

/// <summary>
/// The complete private state of one compensation scope execution: phase, forward cursor and the journal of
/// completed forward occurrences. Entries are never removed while the workflow exists.
/// </summary>
internal sealed record CompensationScopeState
{
    [JsonConstructor]
    public CompensationScopeState(
        CompensationScopePhase phase,
        int nextStepIndex,
        int nextOrdinal,
        CompensationRecoveryCause? cause = null,
        IReadOnlyList<CompensationEntry>? entries = null)
    {
        Phase = phase;
        NextStepIndex = nextStepIndex;
        NextOrdinal = nextOrdinal;
        Cause = cause;
        Entries = entries ?? [];
    }

    public static CompensationScopeState Initial { get; } = new(CompensationScopePhase.Running, 0, 1);

    public CompensationScopePhase Phase { get; init; }

    /// <summary>The index of the next forward step to schedule; the running step is the one before it.</summary>
    public int NextStepIndex { get; init; }

    /// <summary>The completion ordinal the next entry receives.</summary>
    public int NextOrdinal { get; init; }

    public CompensationRecoveryCause? Cause { get; init; }

    public IReadOnlyList<CompensationEntry> Entries { get; init; }
}

/// <summary>One journal entry: a completed forward occurrence and its recovery progress.</summary>
internal sealed record CompensationEntry
{
    [JsonConstructor]
    public CompensationEntry(
        string id,
        string step,
        int ordinal,
        CompensationEntryStatus status,
        string forwardActivityExecutionId,
        string? forwardIterationId = null,
        IReadOnlyList<string>? handlerActivityExecutionIds = null)
    {
        Id = id;
        Step = step;
        Ordinal = ordinal;
        Status = status;
        ForwardActivityExecutionId = forwardActivityExecutionId;
        ForwardIterationId = forwardIterationId;
        HandlerActivityExecutionIds = handlerActivityExecutionIds ?? [];
    }

    public string Id { get; init; }
    public string Step { get; init; }
    public int Ordinal { get; init; }
    public CompensationEntryStatus Status { get; init; }
    public string ForwardActivityExecutionId { get; init; }
    public string? ForwardIterationId { get; init; }
    public IReadOnlyList<string> HandlerActivityExecutionIds { get; init; }
}

/// <summary>Why recovery started. M1 records step faults; the fault's code and message stay on the incident.</summary>
internal sealed record CompensationRecoveryCause
{
    public const string FaultKind = "fault";

    [JsonConstructor]
    public CompensationRecoveryCause(string kind, string step, string activityExecutionId, string? incidentId = null)
    {
        Kind = kind;
        Step = step;
        ActivityExecutionId = activityExecutionId;
        IncidentId = incidentId;
    }

    public string Kind { get; init; }
    public string Step { get; init; }
    public string ActivityExecutionId { get; init; }
    public string? IncidentId { get; init; }
}
