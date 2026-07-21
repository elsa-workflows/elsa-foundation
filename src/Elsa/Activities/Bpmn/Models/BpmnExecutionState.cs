using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// The BPMN engine's typed, versioned private-state envelope payload (<c>Elsa.Bpmn.ExecutionState</c>,
/// schema version 1). All record ids are a pure function of <see cref="Sequence"/>; the only mutation
/// home is <c>BpmnStateMutator</c>.
/// </summary>
public sealed record BpmnExecutionState
{
    [JsonConstructor]
    public BpmnExecutionState(
        IReadOnlyCollection<BpmnToken>? tokens = null,
        IReadOnlyCollection<BpmnActiveChild>? activeChildren = null,
        IReadOnlyCollection<BpmnDiagnosticEvent>? diagnostics = null,
        int sequence = 0,
        bool terminated = false,
        BpmnPendingFault? pendingFault = null,
        IReadOnlyCollection<BpmnEventRace>? races = null)
    {
        Tokens = tokens ?? [];
        ActiveChildren = activeChildren ?? [];
        Diagnostics = diagnostics ?? [];
        Sequence = sequence;
        Terminated = terminated;
        PendingFault = pendingFault;
        Races = races ?? [];
    }

    public IReadOnlyCollection<BpmnToken> Tokens { get; init; }
    public IReadOnlyCollection<BpmnActiveChild> ActiveChildren { get; init; }
    public IReadOnlyCollection<BpmnDiagnosticEvent> Diagnostics { get; init; }
    public int Sequence { get; init; }

    /// <summary>The open/resolved first-catch-wins races opened by event-based gateways (spec 119); additive, schema stays v1.</summary>
    public IReadOnlyCollection<BpmnEventRace> Races { get; init; }

    /// <summary>Set when a terminate end event ended the process; late child completions are ignored.</summary>
    public bool Terminated { get; init; }

    /// <summary>
    /// A fault decision that could not be returned as a terminal continuation because the same
    /// evaluation had already staged child schedules (the runtime forbids terminal decisions that also
    /// schedule children). The next callback surfaces it.
    /// </summary>
    public BpmnPendingFault? PendingFault { get; init; }
}

/// <summary>A deferred fault decision carried on the execution state (see <see cref="BpmnExecutionState.PendingFault"/>).</summary>
public sealed record BpmnPendingFault(string FaultCode, string Message);
