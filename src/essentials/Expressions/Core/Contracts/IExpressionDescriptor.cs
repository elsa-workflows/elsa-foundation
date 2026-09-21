using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

public interface IExpressionDescriptor
{
    string TypeName { get; }

    string DisplayName { get; }
    ExpressionEditingMode EditingMode { get; }

    IDictionary<string, object> Properties { get; }
}
