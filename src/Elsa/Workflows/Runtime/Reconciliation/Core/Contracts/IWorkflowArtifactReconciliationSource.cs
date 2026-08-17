using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;

/// <summary>
/// One closure envelope as read from a source, together with where it came from.
/// </summary>
/// <param name="Origin">
/// Human-meaningful provenance for diagnostics — a file path for the JSON source, a blob URI or an OCI digest for
/// a future one. Never parsed: it exists so an operator can find the offending input, not so the pipeline can
/// derive anything from it.
/// </param>
/// <param name="Closure">The envelope itself, already parsed by the source.</param>
public sealed record WorkflowArtifactClosureFile(string Origin, WorkflowArtifactClosure Closure);

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
    /// The tenant stamped on every reference this source's artifacts mint, or <see langword="null"/> for the
    /// untenanted default.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than required because tenancy is a source <em>configuration</em> axis, not part of a
    /// source's identity: a source that has no tenant concept should not have to say so. Per-tenant fan-out (one
    /// source serving several tenants) is deferred — FR-B-002 — so this is one value per source, not a set.
    /// </remarks>
    string? TenantId => null;

    /// <summary>
    /// Reads the closures this source currently offers. Called once per reconcile pass and enumerated lazily, so a
    /// re-run picks up whatever the mount holds at that moment.
    /// </summary>
    /// <remarks>
    /// Failures that make the <em>pass</em> meaningless (a configured folder that does not exist) throw
    /// <see cref="Exceptions.WorkflowArtifactReconciliationException"/>. Failures scoped to one input (unreadable
    /// file, malformed JSON, unknown format version) throw
    /// <see cref="Exceptions.InvalidWorkflowArtifactClosureException"/> carrying that input's origin. Per-artifact
    /// rejections are never exceptions — they are diagnostics on the pass result.
    /// </remarks>
    IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(CancellationToken cancellationToken = default);
}
