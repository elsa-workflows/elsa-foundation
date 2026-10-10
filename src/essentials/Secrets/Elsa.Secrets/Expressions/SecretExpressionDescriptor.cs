using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Expressions;

public sealed class SecretExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => SecretExpressionTypes.Secret;
    public string DisplayName => "Secret";
    public ExpressionEditingMode EditingMode => ExpressionEditingMode.Reference;
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>
    {
        ["UIHint"] = SecretInputUIHints.SecretPicker
    };
}
