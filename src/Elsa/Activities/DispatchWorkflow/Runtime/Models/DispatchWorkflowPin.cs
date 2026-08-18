using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>Exact runtime-owned executable identity and portable child attribution pinned into a parent executable node.</summary>
/// <remarks>
/// A pin lives in node metadata, and node metadata is content-hash input (ADR 0038). Anything carried here therefore
/// participates in the parent's artifact identity, so the pin carries <b>only</b> facts two engines compiling the same
/// authored content must agree on. Which local source reference serves the child is resolved at dispatch time instead.
/// </remarks>
public sealed record DispatchWorkflowPin(
    WorkflowExecutableIdentity Executable,
    DispatchWorkflowPinProvenance? Source = null);

/// <summary>
/// The portable subset of <see cref="WorkflowExecutableSourceProvenance"/>: the authored child facts that are identical
/// on every engine and are therefore legitimate content-hash input.
/// </summary>
/// <remarks>
/// <para>
/// The excluded members are engine-local — <c>SourceReferenceId</c>, <c>PublicationId</c> and <c>SlotId</c> are minted by
/// whichever engine published the child, <c>SourceKind</c>/<c>SourceId</c> describe how <em>that</em> engine learned about
/// it (a publish and an import of the same child disagree), and <c>SourceVersion</c> is the label of the source system
/// rather than of the artifact. Hashing any of them made the same authored parent hash differently per engine, which is
/// ADR 0038's invariant failing in the <em>equal behaviour, unequal hash</em> direction, and shipped an identifier across
/// an export that the importing engine cannot resolve.
/// </para>
/// <para>
/// These three are deliberately <b>not</b> read off <see cref="DispatchWorkflowPin.Executable"/>, whose same-named members
/// describe the deduplicated content artifact — which can attribute to whichever source first produced identical bytes —
/// rather than the child publication the author actually named.
/// </para>
/// </remarks>
public sealed record DispatchWorkflowPinProvenance(
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion)
{
    public static DispatchWorkflowPinProvenance From(WorkflowExecutableSourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(reference.DefinitionId, reference.DefinitionVersionId, reference.ArtifactVersion);
    }
}
