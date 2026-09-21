using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;

/// <summary>
/// One closure envelope as read from a source, together with where it came from.
/// </summary>
/// <param name="Origin">
/// Human-meaningful provenance for diagnostics — a file path for the JSON source, a blob URI or an OCI digest for
/// a future one. Never parsed: it exists so an operator can find the offending input, not so the pipeline can
/// derive anything from it.
/// </param>
/// <param name="Closure">The envelope itself, already parsed by the source; absent only when <paramref name="ReadError"/> is present.</param>
/// <param name="TenantId">
/// The tenant to stamp on every source reference minted from this file, or <see langword="null"/> for the
/// untenanted default (FR-B-002).
/// </param>
/// <remarks>
/// Tenancy rides on the file rather than on <see cref="IWorkflowArtifactReconciliationSource"/> so the contract
/// keeps exactly the three members T052 pins. A defaulted interface member would have made every future source
/// silently untenanted unless it remembered to opt in — and widening a public <c>.Core</c> contract to carry
/// configuration is a change that is cheap now and breaking later. Duplicating one string per file costs nothing.
/// <para>
/// <b>Scope</b>: this is the tenant of the minted <em>source reference</em>. It is not an activation-slot key —
/// the slot is deliberately untenanted, matching the untenanted trigger bindings it projects into — and it is not
/// the execution tenant, which is stamped separately at execution time. Its one consumer is the same-artifact
/// no-op comparison, which without it would read every imported reference as untenanted and refuse to no-op on a
/// genuine same-tenant re-import. Per-tenant fan-out is deferred.
/// </para>
/// </remarks>
/// <param name="ReadError">
/// A failure scoped to this input. Sources yield it instead of throwing so enumeration can continue with later
/// inputs; pass-wide failures still throw <see cref="WorkflowArtifactReconciliationException"/>.
/// </param>
public sealed record WorkflowArtifactClosureFile(
    string Origin,
    WorkflowArtifactClosure? Closure,
    string? TenantId = null,
    InvalidWorkflowArtifactClosureException? ReadError = null);

/// <summary>
/// A source of portable workflow-executable closures for the runtime-side reconciliation lifecycle (FR-B-002).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the design-side <c>IWorkflowReconciliationSource</c>'s self-identification shape: the source carries
/// its own identity rather than having it configured from the outside, because <see cref="SourceId"/> becomes the
/// activation <b>ownership</b> descriptor — two sources pointed at different folders are different owners, and one
/// may not silently take over the other's definitions.
/// </para>
/// <para>
/// This is a <b>fan-in</b> contract: sources contribute, they never replace each other. The reconciler runs one
/// pass per registered source.
/// </para>
/// </remarks>
public interface IWorkflowArtifactReconciliationSource
{
    /// <summary>Required, self-identifying. Becomes the activation source id, so it must be stable across restarts.</summary>
    string SourceId { get; }

    /// <summary>The kind of source, e.g. <c>"Json"</c>. Stamped on every minted source reference as provenance.</summary>
    string SourceKind { get; }

    /// <summary>
    /// Reads the closures this source currently offers. Called once per reconcile pass and enumerated lazily, so a
    /// re-run picks up whatever the mount holds at that moment.
    /// </summary>
    /// <remarks>
    /// Failures that make the <em>pass</em> meaningless (a configured folder that does not exist) throw
    /// <see cref="WorkflowArtifactReconciliationException"/>. Failures scoped to one input (unreadable file,
    /// malformed JSON, unknown format version) are yielded in
    /// <see cref="WorkflowArtifactClosureFile.ReadError"/> so later inputs still run. Per-artifact rejections are
    /// never exceptions — they are diagnostics on the pass result.
    /// </remarks>
    IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(CancellationToken cancellationToken = default);
}
