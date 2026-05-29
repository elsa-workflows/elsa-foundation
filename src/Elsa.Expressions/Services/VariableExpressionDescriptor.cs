using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionDescriptor : IExpressionDescriptor
{
    public string TypeName => WellKnownExpressionDescriptorTypes.Variable;

    public string DisplayName => WellKnownExpressionDescriptorTypes.Variable;

    public Func<IServiceProvider, IExpressionHandler> HandlerFactory => throw new NotImplementedException();

    public IDictionary<string, object> Properties => throw new NotImplementedException();
}
