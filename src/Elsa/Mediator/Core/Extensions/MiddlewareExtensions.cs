using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using SharedMiddleware = Elsa.Pipelines.Core.Extensions.MiddlewareExtensions;

namespace Elsa.Mediator.Core.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>
    /// Adds middleware to the command pipeline.
    /// </summary>
    public static ICommandPipelineBuilder UseMiddleware<TMiddleware>(this ICommandPipelineBuilder builder, params object[] args) where TMiddleware : ICommandMiddleware
    {
        return builder.Use(next => SharedMiddleware.BuildMiddlewareDelegate<TMiddleware, CommandMiddlewareDelegate>(builder.ApplicationServices, next, args));
    }

    /// <summary>
    /// Adds middleware to the request pipeline.
    /// </summary>
    public static IRequestPipelineBuilder UseMiddleware<TMiddleware>(this IRequestPipelineBuilder builder, params object[] args) where TMiddleware : IRequestMiddleware
    {
        return builder.Use(next => SharedMiddleware.BuildMiddlewareDelegate<TMiddleware, RequestMiddlewareDelegate>(builder.ApplicationServices, next, args));
    }
}
