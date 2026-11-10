using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;

namespace Elsa.Workflows.Design.Validations.Handlers;

/// <summary>The single aggregator for module-owned input-source restrictions.</summary>
internal sealed class CollectInputConstraints(IEnumerable<IActivityInputSourceConstraintProvider> providers)
    : IEventHandler<InputConstraintsCollecting>
{
    public async Task Handle(InputConstraintsCollecting @event, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await provider.GetConstraintAsync(@event.ActivityVersion, @event.InputKey, cancellationToken) is { } constraint)
                @event.Constraints.Add(constraint);
        }
    }
}
