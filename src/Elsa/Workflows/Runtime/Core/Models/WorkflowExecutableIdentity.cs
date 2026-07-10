namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The identity a workflow execution pins to when it dispatches (the "pinned executable"). Under ADR 0038 the
/// artifact's OWN identity is content-addressed — <see cref="ArtifactId"/> plus <see cref="ArtifactHash"/> — and
/// source provenance (definition/version, artifact-version label) lives on the
/// <see cref="WorkflowExecutableSourceReference"/> that points at the artifact.
/// </summary>
/// <remarks>
/// This type still carries the source facts (<see cref="DefinitionId"/>, <see cref="DefinitionVersionId"/>,
/// <see cref="ArtifactVersion"/>) because it doubles as the runtime pinned-snapshot carrier threaded through
/// dispatch, scheduling, checkpointing and persisted <c>WorkflowExecutionState</c>. Re-shaping that carrier to
/// resolve provenance through the reference at dispatch time is the dispatch-semantics work owned by the
/// reference-driven-dispatch slice (W3); this slice only removes the redundant embedded source-reference object,
/// which the standalone <see cref="WorkflowExecutableSourceReference"/> entity now owns.
/// </remarks>
public sealed record WorkflowExecutableIdentity(
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash);
