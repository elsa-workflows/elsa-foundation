using Elsa.Workflows.Runtime.Reconciliation.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Contracts;

/// <summary>
/// Decides how a reconciliation pass <b>reports</b> a definition whose activation slot a different source owns
/// (T124). The built-in default skips and logs; a deployment that expects a mount to always win its definitions
/// can make the same condition a rejection instead.
/// </summary>
/// <remarks>
/// <para>
/// A <b>policy</b>, not a validator (§E6): it returns a course of action rather than a valid/invalid verdict, and
/// nothing about the artifact is in question — the closure passed every gate and is in the content-addressed
/// store. The shape follows <c>IActivityTemplateAdmissionPolicy</c> and its permissive
/// <c>AcceptAllActivityTemplateAdmissionPolicy</c> default.
/// </para>
/// <para>
/// <b>The runtime still knows only "foreign", and that is deliberate.</b> Reconciliation cannot distinguish a
/// publishing owner from another mount without the comparison §E2.2 forbids, and it must not try. The policy is
/// handed the incumbent <c>WorkflowActivationSource</c>, the candidate source and the definition, so a deployment
/// that <em>does</em> know what its own source ids mean can act on that knowledge without the runtime learning
/// any of it. A <c>Kind == "publishing"</c> test belongs in a host's policy; there is none anywhere in Runtime.
/// </para>
/// <para>
/// <b>It cannot make reconciliation a claimant.</b> The answer space is
/// <see cref="ArtifactForeignOwnerDecision"/>'s closed pair — skip or reject — the call happens only after the
/// activation attempt was already refused, and the answer is consumed by a static mapping onto a report entry.
/// See that type's remarks for why each of the three matters. Reconciliation never reclaims a slot another source
/// owns; the only way back to the mount is the owning source releasing it.
/// </para>
/// <para>
/// <b>§2.6.2 replacement contract.</b> Exactly one implementation is meaningful per engine, registered with
/// <c>TryAddScoped</c>, so replacement is first-wins: register yours <em>before</em> composing the
/// reconciliation feature, or <c>services.Replace(...)</c> afterwards. Declared in this domain's
/// <c>EXTENSION_POINTS.md</c> under <em>Overridable contracts</em>.
/// </para>
/// </remarks>
public interface IArtifactForeignOwnerPolicy
{
    /// <summary>
    /// Answers how the pass should report <paramref name="context"/>. Called once per refused artifact, on a path
    /// where activation has already been refused; the slot does not move whatever the answer is.
    /// </summary>
    ValueTask<ArtifactForeignOwnerDecision> DecideAsync(
        ArtifactForeignOwnerContext context,
        CancellationToken cancellationToken = default);
}
