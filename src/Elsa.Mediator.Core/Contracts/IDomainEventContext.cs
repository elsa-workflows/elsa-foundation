namespace Elsa.Mediator.Core.Contracts;

public interface IDomainEventContext
{
    IServiceProvider ServiceProvider { get; }

    IDomainEvent Event { get; }

    CancellationToken CancellationToken { get; }

    /// <summary>
    /// The handler the pipeline is currently dispatching against. Set by the
    /// handler-iterator middleware before invoking the per-handler portion of
    /// the pipeline; read by the invoker and by the exception-shielding
    /// middleware (for diagnostic context). Null outside a per-handler scope.
    /// </summary>
    IDomainEventHandler? CurrentHandler { get; set; }
}
