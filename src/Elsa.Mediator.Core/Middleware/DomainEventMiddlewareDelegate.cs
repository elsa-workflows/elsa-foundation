
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Core.Middleware;

/// <summary>
/// Represents a domain event middleware delegate.
/// </summary>
public delegate ValueTask DomainEventMiddlewareDelegate(IDomainEventContext context);