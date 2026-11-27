using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Events.Core.Contracts;

/// <summary>A middleware component composed into the event pipeline.</summary>
public interface IEventMiddleware : IMiddleware
{
    ValueTask Invoke(IEventContext context);
}
