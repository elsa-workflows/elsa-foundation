namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The set of trigger bindings the indexer just wrote for a single artifact (spec 089 B). It is handed to
/// every <see cref="Contracts.IWorkflowTriggerIndexObserver"/> after the artifact's prior bindings were
/// deleted and the current ones saved, so a consumer can refresh a projection derived from the trigger index
/// (e.g. the HTTP route table) as part of the same publish.
/// </summary>
/// <param name="ArtifactId">The published artifact whose bindings were just (re)indexed.</param>
/// <param name="Bindings">
/// The artifact's new binding set — the exact collection now durable for it (empty when the artifact declares
/// no start-triggers). This is the post-supersession set: any binding from a prior version is already gone.
/// </param>
public sealed record WorkflowTriggerIndexSnapshot(
    string ArtifactId,
    IReadOnlyCollection<WorkflowTriggerBinding> Bindings);
