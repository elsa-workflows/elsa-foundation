namespace Elsa.Workflows.Primitives.Models;

/// <summary>
/// A lightweight, dependency-free identity of a workflow definition version — usable anywhere.
/// Serves as the descriptor for the Workflow activity kind: a catalog row for a workflow marked
/// "usable as activity" carries a <see cref="WorkflowIdentity"/> as its descriptor, and the runtime
/// <c>WorkflowActivityConstructor</c> uses it to configure the single backing
/// <c>WorkflowDefinitionActivity</c>. Lives in the zero-dependency <c>Elsa.Workflows.Primitives</c>
/// building-block library so both the design-side producer and the runtime-side consumer can
/// reference it without a feature → feature dependency.
/// </summary>
/// <param name="DefinitionId">Stable workflow definition identity (shared across all versions).</param>
/// <param name="VersionId">Durable row id of the specific workflow definition version (used to load it).</param>
/// <param name="Version">The version as a SemVer 2.0.0 string (the workflow definition version's version).</param>
public sealed record WorkflowIdentity(string DefinitionId, string VersionId, string Version);
