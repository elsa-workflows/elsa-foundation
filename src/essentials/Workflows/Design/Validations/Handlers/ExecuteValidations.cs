using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;

namespace Elsa.Workflows.Design.Validations.Handlers;

/// <summary>
/// The single <c>DraftValidating</c> handler (Sipke's 2026-06-01 one-handler proposal).
/// Resolves every registered <see cref="IDraftValidator"/>, runs each against the post-mutation
/// Draft, and aggregates the returned errors onto the event. Replaces the former one-event-handler
/// -per-validator sprawl; features now contribute by registering an <see cref="IDraftValidator"/>.
/// </summary>
public sealed class ExecuteValidations(IEnumerable<IDraftValidator> validators) : IEventHandler<DraftValidating>
{
    public async Task Handle(DraftValidating domainEvent, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            var errors = await validator.Validate(domainEvent.Draft, cancellationToken);

            foreach (var error in errors)
                domainEvent.Errors.Add(error);
        }
    }
}
