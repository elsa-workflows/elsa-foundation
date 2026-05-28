using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Filters
{
    /// <summary>
    /// A specification to use when finding workflow definitions. Only non-null fields will be included in the conditional expression.
    /// </summary>
    public class WorkflowDefinitionFilter : IFilter<WorkflowDefinition>
    {
        /// <summary>
        /// Filter by the ID of the workflow definition.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Filter by the IDs of the workflow definitions.
        /// </summary>
        public ICollection<string>? Ids { get; set; }

        /// <summary>
        /// Filter by the name of the workflow definition.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Filter by the name or id of the workflow definition.
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// Filter by the description of the workflow definition.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Filter by the names of the workflow definitions.
        /// </summary>
        public ICollection<string>? Names { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include tenant matching in the filter.
        /// </summary>
        public bool? TenantAgnostic { get; set; }

        /// <summary>
        /// Applies the filter to the specified queryable.
        /// </summary>
        /// <param name="queryable">The queryable to apply the filter to.</param>
        /// <returns>The filtered queryable.</returns>
        public virtual IQueryable<WorkflowDefinition> Apply(IQueryable<WorkflowDefinition> queryable)
        {
            if (Id != null) queryable = queryable.Where(x => x.Id == Id);
            if (Ids != null) queryable = queryable.Where(x => Ids.Contains(x.Id));
            if (Name != null) queryable = queryable.Where(x => x.Name == Name);
            if (Names != null) queryable = queryable.Where(x => Names.Contains(x.Name!));
            if (!string.IsNullOrWhiteSpace(SearchTerm)) queryable = queryable.Where(x => x.Name!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Description!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Id.Contains(SearchTerm));
            if (Description != null) queryable = queryable.Where(x => x.Description == Description);

            return queryable;
        }
    }
}
