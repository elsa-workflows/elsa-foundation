using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => WellKnownExpressionDescriptorTypes.Variable;

    public string DisplayName => WellKnownExpressionDescriptorTypes.Variable;

    public Func<IServiceProvider, IExpressionHandler> HandlerFactory => ActivatorUtilities.GetServiceOrCreateInstance<VariableExpressionHandler>;

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}
