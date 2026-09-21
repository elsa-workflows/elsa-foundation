namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Tunables for live dispatch admission control (RB1, #1235). Bounds the work in flight against the shared durable
/// writer so a burst of concurrent commands cannot overwhelm the host: past the limit a command is <b>refused</b>
/// rather than parked behind the writer's connection gate. This is the same intent
/// <c>RuntimeResumptionOptions</c> already applies to the recovery sweep, ported to live dispatch, which nothing
/// bounded — <c>MaxDrainCycles</c> bounds cycles within one drain, <c>MaxWorkItems</c> bounds items within one cycle,
/// and <c>RuntimeActorEvictionOptions</c> bounds memory.
///
/// <para><b>Why the limit moves.</b> The right concurrency is a property of the deployment, not of Elsa: it differs
/// between SQLite on a laptop, PostgreSQL on a large host, and a network filesystem. A constant baked from one
/// benchmark capture is wrong nearly everywhere while looking authoritative, so the controller discovers its own
/// limit from completion latency. <see cref="StaticLimit"/> pins it for operators who need predictability, and it
/// pins by clamping the same controller to a single value rather than selecting a second implementation, so the
/// tested path and the shipped path cannot diverge.</para>
///
/// <para><b>Why the unit is a dispatch.</b> An <c>External</c>-leaf workflow pays ~56 scheduler dispatches and ~11
/// checkpoint commits per run against a fusable shape's ~5 and ~1 (#1225). A limit counting runs would therefore
/// admit roughly an order of magnitude more work for the shape production traffic actually has while reporting the
/// same number, which makes the limit meaningless for exactly the case it exists to bound.</para>
/// </summary>
public sealed class RuntimeAdmissionOptions
{
    /// <summary>
    /// Whether a command may be shed. When false the controller still evaluates every command and still counts the
    /// decision, so the admitted/shed counters stay meaningful and the code path stays the shipped one; it simply
    /// never refuses. This is the kill switch, not a bypass.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Operator override pinning the limit to a fixed number of dispatch units. Setting it clamps
    /// <see cref="MinLimit"/> and <see cref="MaxLimit"/> to the same value, so adaptation still runs and still
    /// reports, but can never move the limit. Null (default) leaves the controller adaptive.
    /// </summary>
    public int? StaticLimit { get; set; }

    /// <summary>
    /// Limit the controller starts from, in dispatch units. Deliberately not a tuned operating point — the
    /// controller leaves it in either direction on its first saturated sample. Its only job is to be permissive
    /// enough that a host which never saturates never sheds.
    /// </summary>
    public int InitialLimit { get; set; } = 512;

    /// <summary>
    /// Floor the adaptive limit may not fall below, in dispatch units. The heaviest measured production-shaped run
    /// (an External leaf over the hot loop) is ~56 dispatches, so a floor of 64 keeps the controller from collapsing
    /// to a limit that cannot admit one such run. It is a safety floor, not a target.
    /// </summary>
    public int MinLimit { get; set; } = 64;

    /// <summary>Ceiling the adaptive limit may not rise above, in dispatch units.</summary>
    public int MaxLimit { get; set; } = 8192;

    /// <summary>
    /// How much slower than the observed baseline a saturated completion may be before the limit is cut. At or below
    /// the tolerance the limit grows by <see cref="IncreaseStep"/>; above it the limit is multiplied by
    /// <see cref="DecreaseFactor"/>.
    /// </summary>
    public double LatencyTolerance { get; set; } = 2.0;

    /// <summary>Multiplicative decrease applied when a saturated completion exceeds <see cref="LatencyTolerance"/>.</summary>
    public double DecreaseFactor { get; set; } = 0.8;

    /// <summary>Additive increase applied when a saturated completion stays within <see cref="LatencyTolerance"/>.</summary>
    public int IncreaseStep { get; set; } = 8;

    /// <summary>
    /// Per-sample upward drift allowed on the latency baseline. Without it the first fast sample a host ever takes
    /// would pin the baseline forever, and a host that legitimately got slower would ratchet its limit to
    /// <see cref="MinLimit"/> and stay there. A genuinely faster sample still resets the baseline immediately.
    /// </summary>
    public double BaselineDecay { get; set; } = 1.02;

    /// <summary>Retry-After hint handed to a shed caller.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(1);
}
