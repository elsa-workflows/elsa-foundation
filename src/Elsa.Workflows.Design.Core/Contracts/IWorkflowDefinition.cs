namespace Elsa.Workflows.Design.Core.Contracts
{
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
        ///
        /// </summary>
        IWorkflowDefinitionDraft? Draft { get; }

        /// <summary>
        /// Creates and returns a shallow copy of the workflow definition.
        /// </summary>
        IWorkflowDefinition ShallowClone();
    }
}
