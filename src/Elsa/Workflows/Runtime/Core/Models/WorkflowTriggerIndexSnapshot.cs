namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A single artifact's current trigger binding set as the indexer processes a publish (spec 089 B). It is
/// handed to every <see cref="Contracts.IWorkflowTriggerIndexValidator"/> BEFORE any write (the extracted,
/// about-to-be-written set — a validator throw fails the publish with the store untouched) and to every
/// <see cref="Contracts.IWorkflowTriggerIndexObserver"/> AFTER the artifact's prior bindings were deleted and
/// the current ones saved, so a consumer can refresh a projection derived from the trigger index (e.g. the
/// HTTP route table) as part of the same publish.
/// </summary>
/// <param name="ArtifactId">The published artifact whose bindings are being (re)indexed.</param>
/// <param name="Bindings">
/// The artifact's new binding set (empty when the artifact declares no start-triggers). At validation time this
/// is the about-to-be-written set; at observation time it is the exact collection now durable — the
/// post-supersession set, any binding from a prior version already gone.
/// </param>
public sealed record WorkflowTriggerIndexSnapshot(
    string ArtifactId,
    IReadOnlyCollection<WorkflowTriggerBinding> Bindings);
