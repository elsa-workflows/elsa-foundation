namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Toggles ReplaySafe hop fusion (ADR 0047 D1+D2 / spec 123). Inside a live coalescing burst, when a dispatched
/// <c>ScheduleActivity</c> targets a node whose pinned contract declares <see cref="SideEffectProfile.ReplaySafe"/>,
/// the runtime runs the schedule → start → invoke stage cores in one dispatch — omitting the intermediate
/// <c>StartActivity</c>/<c>InvokeActivity</c> queue round-trips — instead of paying one drain hop per stage.
///
/// <para><b>Correctness dial, not a feature switch.</b> The default (<see cref="Enabled"/> = <see langword="true"/>,
/// ratification resolution #3) is the shipped behavior. Setting it to <see langword="false"/> forces the discrete
/// hop chain everywhere and MUST commit byte-identical durable state — the spec-123 guardrail asserts exactly this,
/// so the flag exists to make that A/B testable and to provide a host kill switch. Fusion changes dispatch locality
/// inside a burst; it never changes the durable truth or its crash redrive.</para>
/// </summary>
public sealed class RuntimeReplaySafeFusionOptions
{
    /// <summary>When true (default), ReplaySafe hop fusion engages inside a live coalescing burst.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true (default) and <see cref="Enabled"/> holds, a fused span also pumps its completion cascade inline
    /// (ADR 0047 D2): the <c>CompleteActivity</c> → parent-completion-evaluation → successor <c>ScheduleActivity</c>
    /// items are claimed from the coalescing overlay and dispatched through the same handlers in the same FIFO order
    /// the drain loop would use, eliding only the per-item drain hop. A fusable successor re-enters the D1 fused pass;
    /// join (fan-in) edges, child-fault evaluations, non-ReplaySafe parents, and the workflow tail
    /// (<c>ContinuationScheduling</c>/<c>Checkpoint</c>) always fall back to the discrete drain loop. Setting this to
    /// <see langword="false"/> keeps D1 (fused schedule → start → invoke) with the discrete cascade — the benchmark's
    /// D1-only column — and MUST also commit byte-identical durable state.
    /// </summary>
    public bool FuseCompletionCascade { get; set; } = true;
}
