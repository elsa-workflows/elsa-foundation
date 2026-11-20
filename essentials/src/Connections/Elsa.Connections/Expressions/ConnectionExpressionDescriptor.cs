using Elsa.Connections.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Connections.Expressions;

internal sealed class ConnectionExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => ConnectionExpressionTypes.Connection;
    public string DisplayName => "Connection";
    public ExpressionEditingMode EditingMode => ExpressionEditingMode.Reference;
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>
    {
        ["UIHint"] = ConnectionInputUIHints.ConnectionPicker
    };
}

internal sealed class ConnectionExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    public IEnumerable<IExpressionDescriptor> GetDescriptors()
    {
        yield return new ConnectionExpressionDescriptor();
    }
}
