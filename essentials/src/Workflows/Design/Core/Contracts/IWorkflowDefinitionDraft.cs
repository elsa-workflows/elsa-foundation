using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionDraft
{
    string Id { get; }

    /// <summary>
    /// Foreign key to the owning workflow definition. Many Drafts may belong to one Definition.
    /// </summary>
    string WorkflowDefinitionId { get; }

    WorkflowDefinitionState State { get; }

    /// <summary>
    /// UTC timestamp when this draft was created
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// UTC timestamp when this draft was last modified
    /// </summary>
    DateTimeOffset LastModifiedAt { get; }
}
