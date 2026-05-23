using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models
{
    /// <summary>
    /// A base type for the <see cref="InputArgument{T}"/> type.
    /// </summary>
    public abstract class InputArgument : Argument
    {
        /// <inheritdoc />
        protected InputArgument(IMemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
        {
            Type = type;
        }

        /// <inheritdoc />
        protected InputArgument(Expression? expression, IMemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
        {
            Expression = expression;
            Type = type;
        }

        /// <summary>
        /// Gets or sets the expression.
        /// </summary>
        public Expression? Expression { get; }

        /// <summary>
        /// Gets the type of the ActivityInput.
        /// </summary>
        [JsonPropertyName("typeName")]
        public Type Type { get; }
    }

    /// <summary>
    /// Represents activity ActivityInput that is evaluated at runtime.
    /// </summary>
    public class InputArgument<T> : InputArgument
    {
        /// <inheritdoc />
        public InputArgument(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference, typeof(T))
        {
        }

        /// <inheritdoc />
        public InputArgument(Func<T> @delegate, IMemoryBlockReference memoryBlockReference)
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public InputArgument(Func<IExpressionExecutionContext, ValueTask<T?>> @delegate, IMemoryBlockReference memoryBlockReference)
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public InputArgument(Func<ValueTask<T?>> @delegate, IMemoryBlockReference memoryBlockReference)
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public InputArgument(Func<IExpressionExecutionContext, T> @delegate, IMemoryBlockReference memoryBlockReference)
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public InputArgument(IVariable variable) : base(new("Variable", variable), variable, typeof(T))
        {
        }

        /// <inheritdoc />
        public InputArgument(OutputArgument output) : base(new("Output", output), output.MemoryBlockReference(), typeof(T))
        {
        }

        /// <inheritdoc />
        public InputArgument(Expression expression, IMemoryBlockReference memoryBlockReference) : base(expression, memoryBlockReference, typeof(T))
        {
        }
    }
}
