using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => WellKnownExpressionDescriptorTypes.Variable;

    public string DisplayName => WellKnownExpressionDescriptorTypes.Variable;

    public ExpressionEditingMode EditingMode => ExpressionEditingMode.Reference;

    public Func<IServiceProvider, IExpressionHandler> HandlerFactory => ActivatorUtilities.GetServiceOrCreateInstance<VariableExpressionHandler>;

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}
