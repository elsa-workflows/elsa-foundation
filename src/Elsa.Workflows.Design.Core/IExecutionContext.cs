using Elsa.Activities.Core.Contracts;
using Elsa.Expressions.Core;

namespace Elsa.Workflows.Design.Core
{
    /// <summary>
    /// Provides a common interface to access the current execution context.
    /// </summary>
    public interface IExecutionContext
    {
        /// <summary>
        /// The unique ID of this execution context.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The activity that is currently being executed.
        /// </summary>
        IActivity Activity { get; }

        /// <summary>
        /// The expression execution context.
        /// </summary>
        IExpressionExecutionContext ExpressionExecutionContext { get; }

        /// <summary>
        /// Returns variables declared in the current execution context.
        /// </summary>
        IEnumerable<IVariable> Variables { get; }

        /// <summary>
        /// A dictionary of values that can be associated with this activity execution context.
        /// </summary>
        IDictionary<string, object> Properties { get; }
    }
}
