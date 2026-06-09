namespace Elsa.Expressions.Core.Contracts;

public interface IExpressionDescriptor
{
    string TypeName { get; }

    string DisplayName { get; }
    Func<IServiceProvider, IExpressionHandler> HandlerFactory { get; }

    IDictionary<string, object> Properties { get; }
}