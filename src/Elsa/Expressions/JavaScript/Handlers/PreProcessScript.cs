using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Handlers;

/// <summary>
/// Single handler for <see cref="OnEvaluatingScript"/>. Iterates every registered
/// <see cref="IScriptPreProcessor"/> and lets each pre-process the execution context before the
/// script runs.
/// </summary>
public sealed class PreProcessScript(IEnumerable<IScriptPreProcessor> preProcessors)
    : IEventHandler<OnEvaluatingScript>
{
    public async Task Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
    {
        foreach (var preProcessor in preProcessors)
            await preProcessor.PreProcess(
                domainEvent.Script,
                domainEvent.ExecutionContext,
                domainEvent.ExpressionContext,
                domainEvent.Options,
                cancellationToken);
    }
}