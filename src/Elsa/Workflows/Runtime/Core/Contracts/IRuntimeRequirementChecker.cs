using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Evaluates an artifact's declared runtime requirements against the registries installed in THIS
/// runtime, across both gate axes:
/// <list type="number">
/// <item>consumer capabilities + durable-value storage drivers (exact ordinal matching); and</item>
/// <item>per-node CLR activity-type presence in the well-known type registry.</item>
/// </list>
/// The two axes never intersect — the capability check is per activation <em>mechanism</em> and does
/// not cover type availability, so either axis alone under-gates (clarification 2026-08-14).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>replacement contract</b> (§2.6.2): exactly one implementation is meaningful per
/// engine, registered with <c>TryAdd</c> so a conflicting registration cannot silently win.
/// </para>
/// <para>
/// Extracted from <c>Elsa.Workflows.Publishing.Api</c>'s deployment preflight, which depended only on
/// runtime types. Publishing is the design-to-runtime bridge, so the check belongs here and is
/// <em>shared</em>, not duplicated: the publishing preflight wraps this service and keeps its own
/// retained-set scoping, views and diagnostics, while the artifact importer calls it per artifact.
/// Evaluation semantics are unchanged by the move (FR-B-005).
/// </para>
/// </remarks>
public interface IRuntimeRequirementChecker
{
    /// <summary>
    /// Evaluates <paramref name="subject"/> and returns one verdict covering both axes.
    /// </summary>
    RuntimeRequirementCheckResult Check(RuntimeRequirementCheckSubject subject);
}
