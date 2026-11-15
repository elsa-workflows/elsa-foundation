namespace Elsa.Primitives.Models;

/// <summary>
/// Selects where an authored activity invocation is executed.
/// </summary>
public enum ActivityExecutionPlacement
{
    /// <summary>Execute the activity as part of the workflow coordinator's normal drain.</summary>
    Inline,

    /// <summary>Execute an explicitly eligible activity through a durable background worker.</summary>
    Background
}
