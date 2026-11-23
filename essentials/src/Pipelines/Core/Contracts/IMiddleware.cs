namespace Elsa.Pipelines.Core.Contracts;

/// <summary>
/// Marker for a middleware component. Message-agnostic: command, request, and event middleware
/// all derive from this so the shared pipeline engine (<c>GetInvokeMethod</c> /
/// <c>BuildMiddlewareDelegate</c>) can compose any of them uniformly.
/// </summary>
public interface IMiddleware
{
}
