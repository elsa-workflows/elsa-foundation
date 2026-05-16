using Elsa.Activities.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Core.Models
{    
    /// <summary>
    /// A base type for the <see cref="ActivityInput{T}"/> type.
    /// </summary>
    public abstract class ActivityInput : Argument
    {
        /// <inheritdoc />
        protected ActivityInput(IMemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
        {
            Type = type;
        }

        /// <inheritdoc />
        protected ActivityInput(Expression? expression, IMemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
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
    public class ActivityInput<T> : ActivityInput
    {
        /// <inheritdoc />
        public ActivityInput(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference, typeof(T))
        {
        }

        /// <inheritdoc />
        public ActivityInput(Func<T> @delegate, IMemoryBlockReference memoryBlockReference) 
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public ActivityInput(Func<IExpressionExecutionContext, ValueTask<T?>> @delegate, IMemoryBlockReference memoryBlockReference) 
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public ActivityInput(Func<ValueTask<T?>> @delegate, IMemoryBlockReference memoryBlockReference) 
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public ActivityInput(Func<IExpressionExecutionContext, T> @delegate, IMemoryBlockReference memoryBlockReference) 
            : this(Expression.DelegateExpression(@delegate), memoryBlockReference)
        {
        }

        /// <inheritdoc />
        public ActivityInput(IVariable variable) : base(new("Variable", variable), variable, typeof(T))
        {
        }

        /// <inheritdoc />
        public ActivityInput(ActivityOutput output) : base(new("Output", output), output.MemoryBlockReference(), typeof(T))
        {
        }

        /// <inheritdoc />
        public ActivityInput(Expression expression, IMemoryBlockReference memoryBlockReference) : base(expression, memoryBlockReference, typeof(T))
        {
        }
    }
}
