using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Minimal hook-only <see cref="IEventPublisher"/>: runs an optional <see cref="OnPublish"/> hook on
/// each published event and captures nothing else. Tests use the hook to simulate validator
/// contributions onto <c>OnDraftValidating</c> (errors are derived state — the command and promotion
/// gates re-run the validation gate through this publisher to derive the current error set). Shared
/// across the Groundwork design-persistence test suites.
/// </summary>
/// <remarks>
/// Named for its behaviour (hook-only) to distinguish it from the richer capturing publisher in the
/// Design.Tests infrastructure project — these are separate test projects and each stays
/// self-contained (no cross-test-project reference).
/// </remarks>
public sealed class HookEventPublisher : IEventPublisher
{
    public Action<IEvent>? OnPublish { get; set; }

    /// <summary>
    /// Convenience: install an <see cref="OnPublish"/> hook that contributes <paramref name="error"/>
    /// onto every <c>OnDraftValidating</c> pass, simulating a validator that always emits it. Replaces
    /// the copy-pasted "if OnDraftValidating add ValidationError" lambda at every call site.
    /// </summary>
    public void ContributeError(ValidationError error) =>
        OnPublish = e =>
        {
            if (e is OnDraftValidating validating)
                validating.Errors.Add(error);
        };

    public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
    {
        OnPublish?.Invoke(@event);
        return Task.CompletedTask;
    }
}
