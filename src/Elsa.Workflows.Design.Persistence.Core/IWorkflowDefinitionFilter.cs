using Elsa.Workflows.Design.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core
{
    public interface IWorkflowDefinitionFilter
    {
        IQueryable<WorkflowDefinition> Apply(IQueryable<WorkflowDefinition> queryable);

        bool TenantAgnostic { get; set; }
    }
}
