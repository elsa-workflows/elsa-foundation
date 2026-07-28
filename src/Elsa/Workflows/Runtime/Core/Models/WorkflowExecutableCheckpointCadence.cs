namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The authored checkpoint-persistence cadence compiled into a workflow executable (ADR 0032 R5). It travels with the
/// content-addressed artifact so the Runtime resolves replay-safety off the pinned executable, never a mutable Design
/// store (§E2.2, §E2.6). Present only when the workflow authored a cadence; absent (<see langword="null"/> on the
/// executable) means the host default applies.
/// </summary>
/// <param name="Mode">The cadence alias: <see cref="ImmediateMode"/> or <see cref="CoalescedMode"/>.</param>
/// <param name="MaxSegmentCheckpoints">
/// The authored bounded segment cap for <see cref="CoalescedMode"/>; <see langword="null"/> defers to the host cap.
/// Ignored under <see cref="ImmediateMode"/>.
/// </param>
public sealed record WorkflowExecutableCheckpointCadence(string Mode, int? MaxSegmentCheckpoints = null)
{
    /// <summary>Every relaxable checkpoint flushes immediately (the safe, narrowest replay window).</summary>
    public const string ImmediateMode = "Immediate";

    /// <summary>Relaxable checkpoints fold within a bounded segment and flush at a durable boundary.</summary>
    public const string CoalescedMode = "Coalesced";

    /// <summary>Whether this cadence selects coalesced persistence.</summary>
    public bool IsCoalesced => string.Equals(Mode, CoalescedMode, StringComparison.Ordinal);
}
