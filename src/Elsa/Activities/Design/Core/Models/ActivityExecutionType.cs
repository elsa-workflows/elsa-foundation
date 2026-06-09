namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Execution shape of an activity — how the runtime drives it. Distinct from
/// <c>ImplementationKind</c> (which CLR / Workflow / etc. backs the activity) and
/// from <c>SourceKind</c> (which provenance source produced the catalog row).
/// </summary>
public enum ActivityExecutionType
{
    /// <summary>
    /// Always run synchronously.
    /// </summary>
    Action,

    /// <summary>
    /// Can be used to trigger new workflows.
    /// </summary>
    Trigger,

    /// <summary>
    /// Always run asynchronously.
    /// </summary>
    Job,

    /// <summary>
    /// Run synchronously by default (like <see cref="Action"/>, but can be configured to run asynchronously (like <see cref="Job"/>).
    /// </summary>
    Task
}