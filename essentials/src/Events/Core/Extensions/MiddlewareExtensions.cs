using Elsa.Events.Core.Contracts;
using Elsa.Pipelines.Core.Contracts;
using SharedMiddleware = Elsa.Pipelines.Core.Extensions.MiddlewareExtensions;

namespace Elsa.Events.Core.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>
    /// Adds middleware to the event pipeline. Thin event-typed wrapper over the shared
    /// <c>UseMiddleware&lt;TContext,TMiddleware&gt;</c> so call sites keep the single-type-parameter
    /// ergonomics while the composition engine stays shared.
    /// </summary>
    public static IPipelineBuilder<IEventContext> UseMiddleware<TMiddleware>(this IPipelineBuilder<IEventContext> builder, params object[] args) where TMiddleware : IEventMiddleware
    {
        return SharedMiddleware.UseMiddleware<IEventContext, TMiddleware>(builder, args);
    }
}
