namespace Elsa.Workflows.Design.Validations;

/// <summary>
/// Configurable options shared by validators that walk an activity tree. Bound via
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/>.
/// </summary>
public sealed class WorkflowDesignValidatorOptions
{
    /// <summary>
    /// Maximum depth the activity-tree walker will descend before stopping. Iterative DFS is
    /// used internally, so this is a safety net against cyclic / malformed Draft data rather
    /// than a guard on the .NET call stack. Default: 100.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 100;
}
