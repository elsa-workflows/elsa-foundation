using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JsonConverters;
using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Handlers;

/// <summary>
/// Contributes the Expressions feature's JSON converters: a <see cref="VariableConverterFactory"/>
/// for <c>Variable&lt;T&gt;</c> payloads (resolves via <see cref="IVariableMapper"/>), and a
/// <see cref="FuncExpressionValueConverter"/> for func-expression value payloads. Replaces the
/// two legacy <c>IPayloadSerializerConverterProvider</c> registrations with one handler that
/// calls <c>AddConverter</c> per converter.
/// </summary>
public sealed class ExpressionsJsonConvertersHandler(IVariableMapper variableMapper)
    : IDomainEventHandler<OnJsonPayloadConvertersInitializing>
{
    public ValueTask Handle(OnJsonPayloadConvertersInitializing domainEvent, CancellationToken cancellationToken)
    {
        domainEvent.AddConverter(new VariableConverterFactory(variableMapper));
        domainEvent.AddConverter(new FuncExpressionValueConverter());
        return ValueTask.CompletedTask;
    }
}
