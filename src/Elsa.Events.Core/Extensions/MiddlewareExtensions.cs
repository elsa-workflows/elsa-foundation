using Elsa.Events.Core.Contracts;
using SharedMiddleware = Elsa.Pipelines.Core.Extensions.MiddlewareExtensions;

namespace Elsa.Events.Core.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>
    /// Adds middleware to the event pipeline.
    /// </summary>
    public static IEventPipelineBuilder UseMiddleware<TMiddleware>(this IEventPipelineBuilder builder, params object[] args) where TMiddleware : IEventMiddleware
    {
        return builder.Use(next => SharedMiddleware.BuildMiddlewareDelegate<TMiddleware, EventMiddlewareDelegate>(builder.ApplicationServices, next, args));
    }
}
