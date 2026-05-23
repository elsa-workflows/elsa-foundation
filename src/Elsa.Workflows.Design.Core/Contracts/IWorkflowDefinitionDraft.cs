namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionDraft
{
    string Id { get; }
    
    IWorkflowDefinitionState State { get; }

    /// <summary>
    /// UTC timestamp when this draft was created
    /// </summary>
     DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// UTC timestamp when this draft was last modified
    /// </summary>
    DateTimeOffset LastModifiedAt { get; }
}
