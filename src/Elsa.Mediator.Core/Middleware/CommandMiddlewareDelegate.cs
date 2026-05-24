using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Core.Middleware;

/// <summary>
/// Represents a command middleware delegate.
/// </summary>
public delegate ValueTask CommandMiddlewareDelegate(ICommandContext context);