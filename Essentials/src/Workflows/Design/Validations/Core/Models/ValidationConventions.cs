namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// Reserved <see cref="ValidationError.Path"/> tokens (R2). Only the workflow-scope sentinel is
/// pinned as a constant this round; per-node paths are composed from live graph ids.
/// </summary>
public static class ValidationPaths
{
    /// <summary>
    /// R2 workflow-scope path: a concern bound to the workflow as a whole rather than a specific
    /// node (e.g. a missing start activity, or the synthetic fault marker).
    /// </summary>
    public const string Workflow = "$workflow";
}

/// <summary>
/// Reserved <see cref="ValidationError.Type"/> categories (R3). Pins the categories with a
/// cross-cutting consumer — the shield synthesizes <see cref="Faulted"/>, and UI/tooling group on
/// <see cref="UnknownActivityVersion"/>; the remaining baseline validator categories stay inline.
/// </summary>
public static class ValidationCategories
{
    /// <summary>
    /// R3 reserved category, never emitted by a validator — synthesized by the shielded read gate
    /// (<see cref="DraftValidationGate.TryDeriveValidationErrorsAsync"/>) when a validator throws.
    /// </summary>
    public const string Faulted = "Validation/Faulted";

    /// <summary>
    /// R3 reserved category (FR-033 2026-07-05 amendment) — emitted by
    /// <c>UnknownActivityVersionValidator</c> when an activity node's <c>ActivityVersionId</c> does
    /// not resolve in the catalog.
    /// </summary>
    public const string UnknownActivityVersion = "Graph/UnknownActivityVersion";
}
