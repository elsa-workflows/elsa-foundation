using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Builds the portable <see cref="WorkflowArtifactClosure"/> for one Published workflow definition version
/// (FR-B-010): the pinned executable, its complete transitive dependency closure, the exporting engine's
/// <c>Published</c>-scope source references, and the trigger bindings those artifacts currently own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Destination-agnostic on purpose.</b> It returns the closure model and nothing else — never bytes, never a
/// file name, never an HTTP concept. Encoding is the runtime-owned closure serializer's job and delivery is
/// <see cref="IWorkflowArtifactExportTarget"/>'s, so one producer serves a download, a folder writer, a blob push,
/// or a test that never leaves memory. Anything transport-shaped on this contract would make the export path
/// un-reusable by exactly the callers FR-B-010a exists for.
/// </para>
/// <para>
/// <b>Published-only (FR-B-011).</b> <c>TestRun</c>-scope references are expiring, tied to a
/// <c>WorkflowTestScope</c>, and carry <c>draft:</c>-prefixed version ids: they describe a snapshot that exists
/// only on the engine that minted it, so they are not portable and are never a valid export subject.
/// </para>
/// <para>
/// <b>The closure is complete or it does not exist.</b> An envelope whose dependency edges only resolve because
/// the importing store happens to already hold a child is a broken export (closure-envelope invariant 2), so a
/// dependency the exporting store cannot produce is a hard failure here rather than a silently thinner file.
/// </para>
/// <para>
/// <b>Failure modes are distinguishable by exception type</b>, because a transport binding this contract has to
/// map them to different responses. All four live in <c>Elsa.Workflows.Publishing.Exceptions</c> and share the
/// abstract <c>WorkflowArtifactClosureException</c> base:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>WorkflowArtifactClosureSourceNotFoundException</c> — the version has no source reference of any scope, or
/// its Published reference points at an artifact the executable store no longer holds. "Unknown version".
/// </description></item>
/// <item><description>
/// <c>WorkflowArtifactClosureNotPublishedException</c> — the version exists but carries no <c>Published</c>-scope
/// reference. The test-run-only case, deliberately distinct from "unknown".
/// </description></item>
/// <item><description>
/// <c>IncompleteWorkflowArtifactClosureException</c> — transitive dependencies are absent from the executable
/// store, or present under a hash other than the one the depending artifact pinned. It carries every unresolved
/// identity as structured data, not only in the message, so a caller can name them.
/// </description></item>
/// <item><description>
/// <c>WorkflowArtifactClosureCycleException</c> — the stored dependency graph contains a cycle. No
/// content-addressed compiler can produce one (a child's hash must exist before a parent that pins it can be
/// hashed), so this reports store corruption rather than a user error.
/// </description></item>
/// </list>
/// </remarks>
public interface IWorkflowArtifactClosureFactory
{
    /// <summary>
    /// Builds the portable closure for the Published artifact behind <paramref name="definitionVersionId"/>.
    /// </summary>
    /// <param name="definitionVersionId">The workflow definition version to export.</param>
    /// <param name="cancellationToken">Cancels the store reads.</param>
    /// <returns>
    /// A self-contained closure stamped with <see cref="WorkflowArtifactClosureFormat.CurrentVersion"/>, rooted at
    /// the Published artifact and carrying every transitively reachable dependency.
    /// </returns>
    Task<WorkflowArtifactClosure> CreateAsync(string definitionVersionId, CancellationToken cancellationToken = default);
}
