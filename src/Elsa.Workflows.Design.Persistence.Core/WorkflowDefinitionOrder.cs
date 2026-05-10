using Elsa.Common.Persistence;
using Elsa.Workflows.Design.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Workflows.Design.Persistence.Core
{
    /// <summary>
    /// Represents the order by which to order the results of a query.
    /// </summary>
    public sealed class WorkflowDefinitionOrder<TProp> : OrderDefinition<WorkflowDefinition, TProp>
    {
        /// <inheritdoc />
        public WorkflowDefinitionOrder()
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="WorkflowDefinitionOrder{TProp}"/> class.
        /// </summary>
        public WorkflowDefinitionOrder(Expression<Func<WorkflowDefinition, TProp>> keySelector, OrderDirection direction) : base(keySelector, direction)
        {
        }
    }
}
