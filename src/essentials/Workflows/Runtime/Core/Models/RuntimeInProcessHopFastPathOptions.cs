namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Toggles the in-process-hop payload short-circuit (spec 109, ADR 0031 follow-up item (a)). When a live drain owns an
/// execution's intent delivery (spec 106), the checkpoint committer publishes the already-materialized continuation
/// <see cref="RuntimeSchedulerWorkItem"/> to the drain-scoped carrier and the scheduler post-commit intent dispatcher
/// takes it from the carrier instead of deserializing the durable intent payload. The durable form stays authoritative
/// at every persistence boundary — this only skips a redundant in-process JSON round-trip.
///
/// <para><b>Correctness dial, not a feature switch.</b> The default (<see cref="Enabled"/> = <see langword="true"/>) is
/// the shipped behavior. Setting it to <see langword="false"/> forces the durable deserialize path everywhere and MUST
/// commit byte-identical durable state — the ADR 0031 follow-up (c) guardrail asserts exactly this, so the flag exists
/// primarily to make that A/B testable and to provide a kill switch. Memory is a cache; the serialized form is truth.</para>
/// </summary>
public sealed class RuntimeInProcessHopFastPathOptions
{
    /// <summary>When true (default), the in-process-hop payload short-circuit engages during a live drain.</summary>
    public bool Enabled { get; set; } = true;
}
