using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities
{
    /// <summary>
    /// Represents a versioned workflow definition.
    /// </summary>
    public sealed class WorkflowDefinition : TenantEntity, IWorkflowDefinition
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// Navigation property to the draft
        /// </summary>
        public WorkflowDefinitionDraft? Draft { get; set; }

        /// <summary>
        /// Id of the draft
        /// </summary>
        public string? DraftId { get; set; }

        IWorkflowDefinitionDraft? IWorkflowDefinition.Draft => Draft;

        /// <summary>
        /// Creates and returns a shallow copy of the workflow definition.
        /// </summary>
        public IWorkflowDefinition ShallowClone() => (WorkflowDefinition)MemberwiseClone();
    }
}
