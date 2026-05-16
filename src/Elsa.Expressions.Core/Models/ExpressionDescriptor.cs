using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models
{
    public class ExpressionDescriptor(string typeName) : IExpressionDescriptor
    {        
        /// <summary>
        /// Gets or sets the syntax name.
        /// </summary>
        public string TypeName { get; } = typeName;

        /// <summary>
        /// Gets or sets the display name of the expression type.
        /// </summary>
        public string DisplayName { get; set; } = default!;

        /// <summary>
        /// Gets or sets the expression handler factory.
        /// </summary>
        public Func<IServiceProvider, IExpressionHandler> HandlerFactory { get; set; } = default!;

        /// <summary>
        /// Gets or sets the expression type properties.
        /// </summary>
        public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}
