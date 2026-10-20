using System.Text.Json.Serialization;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>The scope's phase. Persisted by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompensationScopePhase>))]
internal enum CompensationScopePhase
{
    Running,
    Compensating,

    /// <summary>A step or handler waits on an operator: its incident stays blocking with Wait for intervention.</summary>
    AwaitingIntervention,
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
    Compensated,

    /// <summary>The forward outcome is unknown: the effect may exist. Never treated as "no effect".</summary>
    Uncertain,

    /// <summary>The handler failed on every attempt; the scope waits for an operator.</summary>
    Failed,

    /// <summary>A Confirm (or <c>confirmOnComplete</c>) ended the entry's eligibility; it is kept as evidence (spec 258).</summary>
    Confirmed
}

/// <summary>What the forward occurrence reported. Kept when the entry's status changes. Persisted by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompensationForwardOutcome>))]
internal enum CompensationForwardOutcome
{
    Completed,
    Uncertain
}

/// <summary>The result of one handler run. Persisted by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompensationAttemptResult>))]
internal enum CompensationAttemptResult
{
    Running,
    Completed,
    Faulted
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
        IReadOnlyList<CompensationEntry>? entries = null,
        IReadOnlyList<CompensationConfirmation>? confirmations = null,
        IReadOnlyList<string>? receivedHandOvers = null,
        IReadOnlyList<CompensationHandOverProgress>? handOversInProgress = null)
    {
        Phase = phase;
        NextStepIndex = nextStepIndex;
        NextOrdinal = nextOrdinal;
        Cause = cause;
        Entries = entries ?? [];
        Confirmations = confirmations ?? [];
        ReceivedHandOvers = receivedHandOvers ?? [];
        HandOversInProgress = handOversInProgress ?? [];
    }

    public static CompensationScopeState Initial { get; } = new(CompensationScopePhase.Running, 0, 1);

    public CompensationScopePhase Phase { get; init; }

    /// <summary>The index of the next forward step to schedule; the running step is the one before it.</summary>
    public int NextStepIndex { get; init; }

    /// <summary>The completion ordinal the next entry receives.</summary>
    public int NextOrdinal { get; init; }

    public CompensationRecoveryCause? Cause { get; init; }

    public IReadOnlyList<CompensationEntry> Entries { get; init; }

    /// <summary>Every Confirm (and <c>confirmOnComplete</c>), with the entries it confirmed. Never removed.</summary>
    public IReadOnlyList<CompensationConfirmation> Confirmations { get; init; }

    /// <summary>Inner scope occurrences whose hand-over was fully journaled; a repeat delivery changes nothing.</summary>
    public IReadOnlyList<string> ReceivedHandOvers { get; init; }

    /// <summary>Inner scope occurrences with some, not all, of their hand-over batches journaled.</summary>
    public IReadOnlyList<CompensationHandOverProgress> HandOversInProgress { get; init; }
}

/// <summary>The hand-over batches journaled so far for one inner scope occurrence.</summary>
internal sealed record CompensationHandOverProgress
{
    [JsonConstructor]
    public CompensationHandOverProgress(string originScope, IReadOnlyList<int>? batches = null)
    {
        OriginScope = originScope;
        Batches = batches ?? [];
    }

    public string OriginScope { get; init; }
    public IReadOnlyList<int> Batches { get; init; }
}

/// <summary>One journal entry: a forward occurrence that completed or whose outcome is unknown, and its recovery progress.</summary>
internal sealed record CompensationEntry
{
    [JsonConstructor]
    public CompensationEntry(
        string id,
        string step,
        int ordinal,
        CompensationEntryStatus status,
        string forwardActivityExecutionId,
        string operationKey,
        CompensationForwardOutcome forwardOutcome = CompensationForwardOutcome.Completed,
        string? forwardIterationId = null,
        IReadOnlyList<CompensationHandlerAttempt>? handlerAttempts = null,
        string? interventionIncidentId = null,
        string? handlerExecutableNodeId = null,
        CompensationEntryOrigin? origin = null)
    {
        Id = id;
        Step = step;
        Ordinal = ordinal;
        Status = status;
        ForwardActivityExecutionId = forwardActivityExecutionId;
        OperationKey = operationKey;
        ForwardOutcome = forwardOutcome;
        ForwardIterationId = forwardIterationId;
        HandlerAttempts = handlerAttempts ?? [];
        InterventionIncidentId = interventionIncidentId;
        HandlerExecutableNodeId = handlerExecutableNodeId;
        Origin = origin;
    }

    public string Id { get; init; }
    public string Step { get; init; }
    public int Ordinal { get; init; }
    public CompensationEntryStatus Status { get; init; }
    public string ForwardActivityExecutionId { get; init; }

    /// <summary>The operation key the forward step was given; its handler targets it.</summary>
    public string OperationKey { get; init; }

    public CompensationForwardOutcome ForwardOutcome { get; init; }
    public string? ForwardIterationId { get; init; }

    /// <summary>Every run of the handler for this entry, oldest first. Only the last may be running.</summary>
    public IReadOnlyList<CompensationHandlerAttempt> HandlerAttempts { get; init; }

    /// <summary>The blocking incident this entry waits on, while the scope waits for intervention.</summary>
    public string? InterventionIncidentId { get; init; }

    /// <summary>
    /// The handler node of an entry handed over from an inner scope, whose step is not in this scope's structure. An
    /// own entry resolves its handler from its step.
    /// </summary>
    public string? HandlerExecutableNodeId { get; init; }

    /// <summary>Where a handed-over entry came from. The first origin is kept through every later hand-over.</summary>
    public CompensationEntryOrigin? Origin { get; init; }
}

/// <summary>The inner scope occurrence and entry a handed-over entry came from.</summary>
internal sealed record CompensationEntryOrigin
{
    [JsonConstructor]
    public CompensationEntryOrigin(string scopeActivityExecutionId, string entryId)
    {
        ScopeActivityExecutionId = scopeActivityExecutionId;
        EntryId = entryId;
    }

    public string ScopeActivityExecutionId { get; init; }
    public string EntryId { get; init; }
}

/// <summary>One confirmation: the occurrence that confirmed (a Confirm, or the scope itself) and the entries it confirmed.</summary>
internal sealed record CompensationConfirmation
{
    [JsonConstructor]
    public CompensationConfirmation(string activityExecutionId, IReadOnlyList<string>? entryIds = null)
    {
        ActivityExecutionId = activityExecutionId;
        EntryIds = entryIds ?? [];
    }

    public string ActivityExecutionId { get; init; }
    public IReadOnlyList<string> EntryIds { get; init; }
}

/// <summary>One run of an entry's handler. Its execution id is recorded when it completes or faults.</summary>
internal sealed record CompensationHandlerAttempt
{
    [JsonConstructor]
    public CompensationHandlerAttempt(CompensationAttemptResult result, string? activityExecutionId = null, string? incidentId = null)
    {
        Result = result;
        ActivityExecutionId = activityExecutionId;
        IncidentId = incidentId;
    }

    public CompensationAttemptResult Result { get; init; }
    public string? ActivityExecutionId { get; init; }
    public string? IncidentId { get; init; }
}

/// <summary>
/// Why recovery started: a step fault, a step whose outcome is unknown, a Compensate activity (explicit), or an inner
/// scope that compensated itself on a Compensate aimed further out (requested). Set once and never overwritten.
/// </summary>
internal sealed record CompensationRecoveryCause
{
    public const string FaultKind = "fault";
    public const string UncertainKind = "uncertain";
    public const string ExplicitKind = "explicit";
    public const string RequestedKind = "requested";

    [JsonConstructor]
    public CompensationRecoveryCause(string kind, string step, string activityExecutionId, string? incidentId = null, string? requestedBy = null, string? target = null)
    {
        Kind = kind;
        Step = step;
        ActivityExecutionId = activityExecutionId;
        IncidentId = incidentId;
        RequestedBy = requestedBy;
        Target = target;
    }

    public string Kind { get; init; }
    public string Step { get; init; }
    public string ActivityExecutionId { get; init; }
    public string? IncidentId { get; init; }

    /// <summary>The Compensate occurrence that asked for recovery, for an explicit cause.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>
    /// The authored id of an outer scope a Compensate aimed at. This scope compensates itself, then faults with
    /// <c>compensation.scope.compensate-requested</c> so the next scope out recovers too (spec 258 FR-314).
    /// </summary>
    public string? Target { get; init; }
}
