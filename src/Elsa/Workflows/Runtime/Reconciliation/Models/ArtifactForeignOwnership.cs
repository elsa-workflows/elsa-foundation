using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Models;

/// <summary>
/// What a reconciliation pass knows about a definition whose activation slot a <b>different</b> source already
/// owns, handed to <c>IArtifactForeignOwnerPolicy</c> so a deployment can decide how loudly to report it.
/// </summary>
/// <param name="WorkflowDefinitionId">The contended definition.</param>
/// <param name="Incumbent">
/// The source that owns the slot, read from the slot's own <see cref="WorkflowActivationSource"/> (P3 — never
/// inferred from an activation-id prefix). The runtime does not interpret it; a deployment that knows what its
/// own sources mean can, which is the entire reason this reaches a policy at all.
/// </param>
/// <param name="Candidate">The reconciliation source that offered the artifact and was refused.</param>
public sealed record ArtifactForeignOwnerContext(
    string WorkflowDefinitionId,
    WorkflowActivationSource Incumbent,
    WorkflowActivationSource Candidate);

/// <summary>
/// The <b>complete</b> answer space of <c>IArtifactForeignOwnerPolicy</c>: report the untaken slot as a named
/// skip, or report it as a rejection.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no takeover answer, and adding one would not help (T124).</b> A policy that could authorise
/// reconciliation to seize a foreign-owned slot would break the never-reclaim half of T118 and let a shell reload
/// silently revert an operator's publish. Three independent things stop it, so the boundary does not rest on this
/// comment:
/// </para>
/// <list type="number">
/// <item>
/// This is a closed set of exactly two instances. The constructor is <see langword="private"/> and the class is
/// sealed, so no third value can be constructed — by a host, or by a future member added to some enum.
/// </item>
/// <item>
/// The policy is consulted <b>after</b> the activation attempt has already been made and refused. By the time a
/// decision exists, the <c>ForeignSource</c> conflict is a value in hand and there is no pending request left for
/// any answer to influence.
/// </item>
/// <item>
/// The decision is consumed by a <see langword="static"/> mapping function that turns it into a report entry.
/// A static method cannot reach the reconciler's injected activation coordinator or activation authority at all,
/// so "decision causes activation" is not something a later edit can write by accident — it does not compile.
/// </item>
/// </list>
/// <para>
/// Takeover therefore stays where T118 put it: behind the explicit
/// <see cref="WorkflowActivationOwnershipIntent.TakeOver"/> that only an operator-initiated command carries, and
/// which reconciliation never passes.
/// </para>
/// </remarks>
public sealed class ArtifactForeignOwnerDecision
{
    /// <summary>
    /// The built-in answer: record a named <c>ForeignSlotOwner</c> skip, which the startup task surfaces at
    /// warning level. The artifact is sound and its closure unit imported; what did not happen is activation.
    /// </summary>
    public static ArtifactForeignOwnerDecision Skip { get; } = new("skip");

    /// <summary>
    /// Record a rejection instead: the deployment considers a mount it cannot activate an error rather than a
    /// tolerated condition. The slot is untouched either way — this changes how the pass reports, not what serves.
    /// </summary>
    public static ArtifactForeignOwnerDecision Reject { get; } = new("reject");

    private ArtifactForeignOwnerDecision(string name) => Name = name;

    /// <summary>A stable lowercase token for diagnostics. Not a discriminator to parse.</summary>
    public string Name { get; }

    public override string ToString() => Name;
}
