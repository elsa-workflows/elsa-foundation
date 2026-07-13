using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Expressions;

public sealed class SecretExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => SecretExpressionTypes.Secret;
    public string DisplayName => "Secret";
    public ExpressionEditingMode EditingMode => ExpressionEditingMode.Reference;
    public Func<IServiceProvider, IExpressionHandler> HandlerFactory => ActivatorUtilities.GetServiceOrCreateInstance<SecretExpressionHandler>;

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>
    {
        ["UIHint"] = SecretInputUIHints.SecretPicker
    };
}
