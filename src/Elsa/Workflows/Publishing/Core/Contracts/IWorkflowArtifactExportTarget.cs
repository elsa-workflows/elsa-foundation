using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>How a target handed the closure over.</summary>
public enum WorkflowArtifactExportDeliveryKind
{
    /// <summary>
    /// The target returned the encoded closure to the caller and wrote nothing anywhere. The safe, repeatable
    /// shape: the v1 <c>download</c> target is one of these, which is what lets a GET bind to it.
    /// </summary>
    InlinePayload,

    /// <summary>
    /// The target delivered the closure somewhere external (a folder, a blob container, a registry) and returned
    /// only a locator. An external side effect, so a transport binding one of these needs its own command surface
    /// with an explicit idempotency contract — never a safe method.
    /// </summary>
    Receipt
}

/// <summary>
/// What one <see cref="IWorkflowArtifactExportTarget"/> produced for one closure.
/// </summary>
/// <remarks>
/// The two shapes are mutually exclusive and the <see cref="Kind"/> says which is populated:
/// <see cref="WorkflowArtifactExportDeliveryKind.InlinePayload"/> fills <see cref="Payload"/> and leaves
/// <see cref="Location"/> null; <see cref="WorkflowArtifactExportDeliveryKind.Receipt"/> does the reverse. Use
/// <see cref="Inline"/> and <see cref="Receipt"/> rather than the positional constructor so the pairing is checked
/// at the point of construction instead of surfacing as a null two layers away.
/// </remarks>
/// <param name="TargetId">The <see cref="IWorkflowArtifactExportTarget.TargetId"/> that produced this delivery.</param>
/// <param name="Kind">Which of the two shapes this delivery is.</param>
/// <param name="Payload">The encoded closure bytes for an inline delivery; otherwise <see langword="null"/>.</param>
/// <param name="Location">Where a receipt-producing target put the closure; otherwise <see langword="null"/>.</param>
public sealed record WorkflowArtifactExportDelivery(
    string TargetId,
    WorkflowArtifactExportDeliveryKind Kind,
    ReadOnlyMemory<byte>? Payload,
    string? Location)
{
    /// <summary>An inline delivery: the encoded closure travels back to the caller, nothing is written.</summary>
    public static WorkflowArtifactExportDelivery Inline(string targetId, ReadOnlyMemory<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return new(targetId, WorkflowArtifactExportDeliveryKind.InlinePayload, payload, Location: null);
    }

    /// <summary>A receipt delivery: the closure went somewhere external and only its locator comes back.</summary>
    public static WorkflowArtifactExportDelivery Receipt(string targetId, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        return new(targetId, WorkflowArtifactExportDeliveryKind.Receipt, Payload: null, location);
    }
}

/// <summary>
/// A destination a portable workflow-artifact closure can be delivered to (FR-B-010a) — the export-side mirror of
/// the import side's <c>IWorkflowArtifactReconciliationSource</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fan-in, never replacement.</b> Targets contribute; a folder writer arriving later must not displace the
/// built-in download. Register with <c>TryAddEnumerable</c> and resolve
/// <c>IEnumerable&lt;IWorkflowArtifactExportTarget&gt;</c>, selecting by <see cref="TargetId"/>. This is the
/// Strategy pattern (framework §2.24.2 #9), and it is the reason the closure factory stays destination-agnostic:
/// producing the closure and deciding where it goes are two decisions with two different lifetimes.
/// </para>
/// <para>
/// <b>Self-identifying, like the import source.</b> <see cref="TargetId"/> is the target's own property rather
/// than a key it is registered under, because a caller that selects a destination is naming a behaviour, not a DI
/// slot, and a registration-supplied key would let the same target answer to two names.
/// </para>
/// <para>
/// <b>Encoding belongs to the target, not to this contract.</b> Targets take the closure model and encode it
/// themselves through the runtime-owned <c>IWorkflowArtifactClosureSerializer</c> — the same codec the JSON import
/// reader decodes with — so export and import are literally one encoder and a round trip cannot drift.
/// </para>
/// </remarks>
public interface IWorkflowArtifactExportTarget
{
    /// <summary>
    /// Stable, self-identifying destination name — <c>"download"</c> for the v1 built-in; <c>"folder"</c> and
    /// <c>"blob"</c> are deferred. Callers select on this, so it must not change across restarts.
    /// </summary>
    string TargetId { get; }

    /// <summary>Delivers <paramref name="closure"/> and reports what happened.</summary>
    /// <param name="closure">The portable closure, already validated and complete by construction.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task<WorkflowArtifactExportDelivery> DeliverAsync(WorkflowArtifactClosure closure, CancellationToken cancellationToken = default);
}
