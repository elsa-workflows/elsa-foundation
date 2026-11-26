using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Linq.Expressions;

namespace Elsa.Workflows.Design.Persistence.Core.OrderDefinitions;

/// <summary>
/// Represents the order by which to order the results of a query.
/// </summary>
public sealed class WorkflowDefinitionVersionOrder<TProp> : OrderDefinition<WorkflowDefinitionVersion, TProp>
{
    /// <inheritdoc />
    public WorkflowDefinitionVersionOrder()
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="WorkflowDefinitionVersionOrder{TProp}"/> class.
    /// </summary>
    public WorkflowDefinitionVersionOrder(Expression<Func<WorkflowDefinitionVersion, TProp>> keySelector, OrderDirection direction)
        : base(keySelector, direction)
    {
    }
}