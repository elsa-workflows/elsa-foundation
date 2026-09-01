using System.Text;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// The v1 built-in export target (FR-B-010a): hands the encoded closure straight back to the caller and writes
/// nothing anywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this one is the only target a GET may bind to.</b> Its delivery kind is
/// <see cref="WorkflowArtifactExportDeliveryKind.InlinePayload"/>, so invoking it has no effect a crawler, a retry or
/// a cache could repeat. The deferred folder-writer and blob-push targets produce
/// <see cref="WorkflowArtifactExportDeliveryKind.Receipt"/> deliveries — external side effects — and arrive with
/// their own POST command surface carrying an explicit idempotency contract.
/// </para>
/// <para>
/// <b>The bytes come from the runtime-owned codec, never from a local <c>JsonSerializer</c> call.</b>
/// <see cref="IWorkflowArtifactClosureSerializer"/> is the same encoder the JSON import reader decodes with, which
/// is what makes an export/import round trip incapable of drifting: there is one wire format because there is one
/// implementation of it. Encoding here rather than on the closure factory is also what keeps that factory
/// destination-agnostic — a target that wrote a different encoding would be a second format nobody declared.
/// </para>
/// <para>
/// UTF-8 without a byte-order mark: the envelope is JSON, RFC 8259 requires UTF-8 on the wire, and a BOM would make
/// the exported bytes differ from the store-round-tripped ones for no semantic reason.
/// </para>
/// </remarks>
public sealed class DownloadWorkflowArtifactExportTarget(IWorkflowArtifactClosureSerializer closureSerializer)
    : IWorkflowArtifactExportTarget
{
    /// <summary>The stable destination name callers select on. Pinned for elsa-foundation-studio#493.</summary>
    public const string Id = "download";

    public string TargetId => Id;

    public Task<WorkflowArtifactExportDelivery> DeliverAsync(
        WorkflowArtifactClosure closure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(closure);
        cancellationToken.ThrowIfCancellationRequested();

        var json = closureSerializer.Serialize(closure);
        return Task.FromResult(WorkflowArtifactExportDelivery.Inline(Id, new UTF8Encoding(false).GetBytes(json)));
    }
}
