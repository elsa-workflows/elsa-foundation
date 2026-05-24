using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Core.Middleware;

/// <summary>
/// Represents a request middleware delegate.
/// </summary>
public delegate ValueTask RequestMiddlewareDelegate(IRequestContext context);