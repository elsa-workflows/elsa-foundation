namespace Elsa.Expressions.Core.Contracts;

using Elsa.Expressions.Core.Models;

public interface IExpressionDescriptor
{
    string TypeName { get; }

    string DisplayName { get; }
    ExpressionEditingMode EditingMode { get; }

    IDictionary<string, object> Properties { get; }
}
