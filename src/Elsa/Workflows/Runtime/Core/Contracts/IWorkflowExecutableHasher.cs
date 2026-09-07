using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Computes the content-addressable identity of a compiled workflow executable: the deterministic
/// SHA-256 <c>ArtifactHash</c> over a canonical rendering of the executable, and the derived
/// <c>ArtifactId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>Elsa.Workflows.Publishing</c> to the Runtime layer (FR-B-010), the third of the
/// three extractions in spec 151. The compiler remains the derivation site on a publish-capable
/// engine; the artifact importer is the second consumer, recomputing each received artifact's hash
/// before persistence.
/// </para>
/// <para>
/// <b>The canonical payload shape is wire-significant</b>: any change here changes every artifact
/// hash and id. Per ADR 0038 the payload is behavioral-only — it covers the canonical node tree and
/// carries no source identity, so equal hash ⇔ equal behaviour in both directions and executables
/// are content-addressed. That invariant is exactly what makes the importer's recompute meaningful:
/// the executable store is create-only and dedups by id, so persisting an unverified payload under a
/// claimed id would let a corrupted file <em>become</em> that id's content on a fresh engine.
/// </para>
/// <para>
/// This is a <b>replacement contract</b> (§2.6.2): exactly one hasher is meaningful per engine, and
/// two implementations would mean two definitions of identity.
/// </para>
/// </remarks>
public interface IWorkflowExecutableHasher
{
    /// <summary>Hashes the node tree alone.</summary>
    string ComputeHash(ExecutableNode rootActivity);

    /// <summary>Hashes the full behavioural payload — node tree, input contract, dependencies, cadence, variables and incident strategy.</summary>
    string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        WorkflowExecutableCheckpointCadence? checkpointCadence = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null,
        IncidentStrategyReference? incidentStrategy = null);

    /// <summary>Derives the content-addressed artifact id from a computed hash.</summary>
    string CreateArtifactId(string artifactIdPrefix, string artifactHash);
}
