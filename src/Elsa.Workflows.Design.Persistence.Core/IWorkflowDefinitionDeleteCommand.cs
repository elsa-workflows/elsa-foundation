using Elsa.Workflows.Design.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core
{
    /// <summary>
    /// Represents a store of <see cref="WorkflowDefinition"/>s.
    /// </summary>
    public interface IWorkflowDefinitionDeleteCommand
    {
        /// <inheritdoc />
        Task<long> DeleteAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);
    }
}
