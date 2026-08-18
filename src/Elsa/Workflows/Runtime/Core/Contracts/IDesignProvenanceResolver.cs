namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Answers whether a design-provenance identifier can be resolved on <b>this</b> engine (FR-B-012).
/// </summary>
/// <remarks>
/// <para>
/// An imported artifact carries design-side identifiers — definition ids, definition-version ids — minted by the
/// engine that authored it. On a runtime-only engine those resolve to nothing. FR-B-012 requires them to render as
/// unresolved rather than to error, and a bare identifier cannot do that: it is indistinguishable from a local one
/// until a caller follows it and gets a 404, which for a UI means drawing a dead link as though it were navigable.
/// </para>
/// <para>
/// <b>Absence is the answer for a runtime-only engine.</b> This contract is resolved <i>optionally</i>: an engine
/// that registers no implementation cannot resolve design provenance, and every design identifier it renders is
/// flagged. That is why the contract lives here and not in a design assembly — <b>§E2.2 forbids Runtime from
/// depending on Design</b>, so the inspection surface must never consult a design store directly. Runtime declares
/// the question; something that legitimately sees both sides answers it.
/// </para>
/// <para>
/// <b>The answer is per request, and never persisted.</b> It describes the engine doing the rendering, not the
/// artifact being rendered — the same artifact is resolvable on a combined engine and unresolvable on a
/// runtime-only one. Stamping it onto the artifact at import would mis-flag it the moment it were imported
/// somewhere the definition exists, and would put an engine-local value into content-hash input, which is exactly
/// the defect ADR 0038 forbids and T093a removed from the dispatch pin.
/// </para>
/// </remarks>
public interface IDesignProvenanceResolver
{
    /// <summary>True when <paramref name="definitionVersionId"/> resolves to a design definition version here.</summary>
    ValueTask<bool> ResolvesAsync(string definitionVersionId, CancellationToken cancellationToken = default);
}
