using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Computes the content-addressable identity of a compiled workflow executable: the deterministic
/// SHA-256 <c>ArtifactHash</c> over a canonical rendering of the executable, and the derived
/// <c>ArtifactId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>Elsa.Workflows.Publishing</c> to the Runtime layer.
/// The compiler remains the derivation site on a publish-capable
/// engine; the artifact importer is the second consumer, recomputing each received artifact's hash
/// before persistence.
/// </para>
/// <para>
/// <b>The canonical payload shape is wire-significant</b>: any change here changes every artifact
/// hash and id. The payload is behavioral-only — it covers the canonical node tree and
/// carries no source identity, so equal hash ⇔ equal behaviour in both directions and executables
/// are content-addressed. That invariant is exactly what makes the importer's recompute meaningful:
/// the executable store is create-only and dedups by id, so persisting an unverified payload under a
/// claimed id would let a corrupted file <em>become</em> that id's content on a fresh engine.
/// </para>
/// <para>
/// New hashes use the current canonical schema. <see cref="MatchesHash"/> may also verify retained artifacts
/// against historical schemas using their original coverage, without changing their identities or bodies.
/// </para>
/// <para>
/// This is a <b>replacement contract</b>: exactly one hasher is meaningful per engine, and
/// two implementations would mean two definitions of identity.
/// </para>
/// </remarks>
public interface IWorkflowExecutableHasher
{
    /// <summary>Hashes the node tree alone.</summary>
    string ComputeHash(ExecutableNode rootActivity);

    /// <summary>Hashes the full behavioural payload — node tree, input contract, dependencies, persistence mode, variables and incident strategy.</summary>
    string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        PersistenceMode? persistenceMode = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null,
        IncidentStrategyReference? incidentStrategy = null);

    /// <summary>
    /// Verifies the declared hash using a canonical schema this implementation supports. The default
    /// verifies the current schema; a hasher that evolves its rendering may also recognize historical schemas.
    /// Verification never changes an artifact's identity or payload.
    /// </summary>
    bool MatchesHash(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return executable.InputContract is { } contract && StringComparer.Ordinal.Equals(
            ComputeHash(executable.RootActivity, contract, executable.Dependencies, executable.PersistenceMode,
                executable.WorkflowVariables, executable.IncidentStrategy), executable.Identity.ArtifactHash);
    }

    /// <summary>Derives the content-addressed artifact id from a computed hash.</summary>
    string CreateArtifactId(string artifactIdPrefix, string artifactHash);
}
