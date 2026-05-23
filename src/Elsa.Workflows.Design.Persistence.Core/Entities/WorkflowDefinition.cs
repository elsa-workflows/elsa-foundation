using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Entities
{
    /// <summary>
    /// Represents a versioned workflow definition.
    /// </summary>
    public sealed class WorkflowDefinition : Entity, IWorkflowDefinition
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        [Immutable]
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastModifiedAt { get; set; }

        /// <summary>
        /// Value object containing some technical and custom meta data about the workflow definition
        /// </summary>
        public WorkflowMetadata? MetaData { get; set; }

        /// <summary>
        /// Navigation property to the draft
        /// </summary>
        public WorkflowDefinitionDraft? Draft { get; set; }

        /// <summary>
        /// Id of the draft
        /// </summary>
        public string? DraftId { get; set; }

        /// <summary>
        /// Creates and returns a shallow copy of the workflow definition.
        /// </summary>
        public IWorkflowDefinition ShallowClone() => (WorkflowDefinition)MemberwiseClone();
    }
}
