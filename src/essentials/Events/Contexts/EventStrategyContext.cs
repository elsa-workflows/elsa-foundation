using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Contexts;

/// <inheritdoc cref="IEventStrategyContext" />
public sealed record EventStrategyContext(
    IEventContext EventContext,
    IEventHandler[] Handlers,
    IServiceProvider ServiceProvider,
    CancellationToken CancellationToken = default
)
    : IEventStrategyContext;
