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
/// Reserved <see cref="ValidationError.Type"/> categories (R3). Only the shield-synthesized fault
/// category is pinned as a constant this round; baseline validator categories stay inline.
/// </summary>
public static class ValidationCategories
{
    /// <summary>
    /// R3 reserved category, never emitted by a validator — synthesized by the shielded read gate
    /// (<see cref="DraftValidationGate.TryDeriveValidationErrorsAsync"/>) when a validator throws.
    /// </summary>
    public const string Faulted = "Validation/Faulted";
}
