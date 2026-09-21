namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinition
{
    /// <summary>
    /// The logical ID of the workflow. This ID is the same across versions.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The name of the workflow.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A short description of what the workflow is about.
    /// </summary>
    string? Description { get; }

    /// <summary>
    ///
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    ///
    /// </summary>
    DateTimeOffset LastModifiedAt { get; }

    /// <summary>
    /// When set, the timestamp at which this logical workflow definition was soft-deleted; <c>null</c>
    /// when live. A definition-level lifecycle metadatum (peer of <see cref="Name"/>/<see cref="CreatedAt"/>),
    /// NOT authored content — it is out of <c>WorkflowDefinitionState</c> scope per constitution §E2.9.2.
    /// Surfaced on the contract so reconciliation can propagate a source's soft-delete flag (spec 085 FR-008).
    /// </summary>
    DateTimeOffset? DeletedAt { get; }

    /// <summary>
    /// Creates and returns a shallow copy of the workflow definition.
    /// </summary>
    IWorkflowDefinition ShallowClone();
}