using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Handlers;

/// <summary>
/// Single handler for <see cref="OnScriptEvaluated"/>. Iterates every registered
/// <see cref="IScriptPostProcessor"/> and lets each act on the execution context after the script
/// has run.
/// </summary>
public sealed class PostProcessScript(IEnumerable<IScriptPostProcessor> postProcessors)
    : IEventHandler<OnScriptEvaluated>
{
    public async Task Handle(OnScriptEvaluated domainEvent, CancellationToken cancellationToken)
    {
        foreach (var postProcessor in postProcessors)
            await postProcessor.PostProcess(
                domainEvent.ExecutionContext,
                domainEvent.ExpressionContext,
                domainEvent.Options,
                cancellationToken);
    }
}