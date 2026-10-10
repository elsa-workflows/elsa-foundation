using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Exceptions;

/// <summary>
/// Base for every reason <c>IWorkflowArtifactClosureFactory</c> refuses to produce a closure (§2.23.5).
/// </summary>
/// <remarks>
/// The family exists so a caller can catch "export refused" in one place, while the derived types stay
/// individually catchable — a transport binding maps each to a different response, so the distinction has to live
/// in the type and not in a parsed message. Every member carries the definition version that was asked for.
/// </remarks>
public abstract class WorkflowArtifactClosureException : InvalidOperationException
{
    protected WorkflowArtifactClosureException(string definitionVersionId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        DefinitionVersionId = definitionVersionId;
    }

    /// <summary>The definition version the export was requested for.</summary>
    public string DefinitionVersionId { get; }
}

/// <summary>
/// Raised when there is nothing to export: the definition version has no source reference of any scope, or the
/// Published reference it does have points at an artifact the executable store no longer holds.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="WorkflowArtifactClosureNotPublishedException"/>. "I have never heard of
/// this version" and "this version exists but was never published" are different answers to an operator, and a
/// transport that collapsed them would tell someone their draft does not exist.
/// </remarks>
public sealed class WorkflowArtifactClosureSourceNotFoundException(string definitionVersionId, string reason)
    : WorkflowArtifactClosureException(definitionVersionId, $"Cannot export workflow definition version '{definitionVersionId}': {reason}")
{
    /// <summary>The cause, without the version prefix, for callers that render their own message.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Raised when the definition version exists but carries no <c>Published</c>-scope source reference (FR-B-011) —
/// in practice, a version that only ever ran as a Test Run.
/// </summary>
/// <remarks>
/// A <c>TestRun</c> reference is expiring, is tied to a <c>WorkflowTestScope</c>, and carries a
/// <c>draft:</c>-prefixed version id, so exporting one would ship a snapshot that is meaningless the moment it
/// leaves the engine that minted it. <see cref="ObservedScopes"/> names what was actually found so the refusal is
/// actionable rather than merely negative.
/// </remarks>
public sealed class WorkflowArtifactClosureNotPublishedException : WorkflowArtifactClosureException
{
    public WorkflowArtifactClosureNotPublishedException(
        string definitionVersionId,
        IReadOnlyCollection<WorkflowExecutableReferenceScope> observedScopes)
        : base(definitionVersionId, CreateMessage(definitionVersionId, observedScopes))
    {
        ObservedScopes = observedScopes.Distinct().Order().ToArray();
    }

    /// <summary>The reference scopes the version actually carries — never <c>Published</c>, or this would not throw.</summary>
    public IReadOnlyList<WorkflowExecutableReferenceScope> ObservedScopes { get; }

    private static string CreateMessage(
        string definitionVersionId,
        IReadOnlyCollection<WorkflowExecutableReferenceScope> observedScopes) =>
        $"Cannot export workflow definition version '{definitionVersionId}': it has no Published source reference. " +
        $"Only {string.Join(", ", observedScopes.Distinct().Order().Select(scope => $"'{scope}'"))}-scope reference(s) exist, " +
        "and those are expiring, engine-local snapshots that are not portable.";
}

/// <summary>
/// Raised when the transitive dependency walk cannot complete: one or more child artifacts are absent from the
/// executable store, or are present under a hash other than the one the depending artifact pinned.
/// </summary>
/// <remarks>
/// <para>
/// Export never emits a thinner closure. The envelope's completeness invariant is environment-independent by
/// contract, so a file that only imports because the destination store happens to already hold the child is the
/// exact failure mode the invariant exists to prevent — it would pass on the machine it was tested on and fail on
/// the one it ships to.
/// </para>
/// <para>
/// <b>Every</b> unresolved edge is reported, not just the first one found, and the identities are structured data
/// on <see cref="MissingArtifactIds"/> / <see cref="MissingArtifacts"/> rather than only text in the message. A
/// caller rendering "the following artifacts are missing" must not have to parse a sentence to do it.
/// </para>
/// </remarks>
public sealed class IncompleteWorkflowArtifactClosureException : WorkflowArtifactClosureException
{
    public IncompleteWorkflowArtifactClosureException(
        string definitionVersionId,
        string rootArtifactId,
        IReadOnlyCollection<MissingWorkflowArtifactReference> missingArtifacts)
        : base(definitionVersionId, CreateMessage(definitionVersionId, rootArtifactId, missingArtifacts))
    {
        RootArtifactId = rootArtifactId;
        MissingArtifacts = missingArtifacts.ToArray();
        MissingArtifactIds = MissingArtifacts.Select(missing => missing.ArtifactId).ToArray();
    }

    /// <summary>The artifact the closure was rooted at.</summary>
    public string RootArtifactId { get; }

    /// <summary>Every unresolved dependency edge, with the hash that was expected and the reason it failed.</summary>
    public IReadOnlyList<MissingWorkflowArtifactReference> MissingArtifacts { get; }

    /// <summary>The unresolved artifact ids alone — what a caller names in an error response.</summary>
    public IReadOnlyList<string> MissingArtifactIds { get; }

    private static string CreateMessage(
        string definitionVersionId,
        string rootArtifactId,
        IReadOnlyCollection<MissingWorkflowArtifactReference> missingArtifacts) =>
        $"Cannot export workflow definition version '{definitionVersionId}': the closure rooted at '{rootArtifactId}' is " +
        $"incomplete. Unresolved dependency artifact(s): {string.Join(", ", missingArtifacts.Select(missing => missing.Describe()))}.";
}

/// <summary>Why one dependency edge could not be resolved out of the exporting engine's executable store.</summary>
/// <param name="ArtifactId">The child artifact the edge points at.</param>
/// <param name="ExpectedArtifactHash">The hash the depending artifact pinned.</param>
/// <param name="StoredArtifactHash">
/// The hash actually stored under <paramref name="ArtifactId"/>, or <see langword="null"/> when the store holds no
/// such artifact at all. A non-null value means the store contradicts the pin, which is a stronger fault than
/// absence: a content-addressed id whose content differs is corruption, not a gap.
/// </param>
/// <param name="DependentArtifactId">The closure member that declared the edge, so the fault is locatable.</param>
public sealed record MissingWorkflowArtifactReference(
    string ArtifactId,
    string ExpectedArtifactHash,
    string? StoredArtifactHash,
    string DependentArtifactId)
{
    /// <summary>One-line rendering used in the exception message.</summary>
    public string Describe() =>
        StoredArtifactHash is null
            ? $"'{ArtifactId}@{ExpectedArtifactHash}' (declared by '{DependentArtifactId}', not in the executable store)"
            : $"'{ArtifactId}@{ExpectedArtifactHash}' (declared by '{DependentArtifactId}', stored hash is '{StoredArtifactHash}')";
}

/// <summary>
/// Raised when the stored dependency graph reachable from the exported root contains a cycle.
/// </summary>
/// <remarks>
/// This should be unreachable through the compiler: an artifact's hash covers its dependency edges, so a child's
/// identity must already exist before a parent that pins it can be hashed, and the back edge can therefore never
/// be formed. It is still detected rather than assumed away, because the walk reads a store any provider may have
/// written and an undetected cycle is either an infinite walk or a silently truncated envelope. Reaching this is a
/// store-corruption report, not a user error.
/// </remarks>
public sealed class WorkflowArtifactClosureCycleException(string definitionVersionId, IReadOnlyCollection<string> cyclePath)
    : WorkflowArtifactClosureException(
        definitionVersionId,
        $"Cannot export workflow definition version '{definitionVersionId}': the stored executable dependency graph " +
        $"contains a cycle ({string.Join(" -> ", cyclePath)}), which no content-addressed artifact can legitimately form.")
{
    /// <summary>The artifact ids forming the cycle, in traversal order, closing on the repeated member.</summary>
    public IReadOnlyList<string> CyclePath { get; } = cyclePath.ToArray();
}

/// <summary>
/// Raised when a store the closure factory reads fails for an infrastructure reason (§2.23.5).
/// </summary>
/// <remarks>
/// The other members of this family are answers about the workflow — a caller maps them to a client error. This
/// one is an answer about the engine, and it exists so that a provider's own exception type never crosses the
/// publishing boundary raw. The original is always preserved as <see cref="Exception.InnerException"/>, and
/// cancellation is deliberately never wrapped: a caller's cancel is not a storage fault.
/// </remarks>
public sealed class WorkflowArtifactClosureStorageException(string definitionVersionId, string operation, Exception innerException)
    : WorkflowArtifactClosureException(
        definitionVersionId,
        $"Cannot export workflow definition version '{definitionVersionId}': the engine failed to {operation}.",
        innerException)
{
    /// <summary>The store read that failed, phrased as an infinitive so it composes into the message.</summary>
    public string Operation { get; } = operation;
}
