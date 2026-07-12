namespace Elsa.Expressions.Core.Contracts;

using Elsa.Expressions.Core.Models;

public interface IExpressionDescriptor
{
    string TypeName { get; }

    string DisplayName { get; }
    ExpressionEditingMode EditingMode { get; }
    Func<IServiceProvider, IExpressionHandler> HandlerFactory { get; }

    IDictionary<string, object> Properties { get; }
}
