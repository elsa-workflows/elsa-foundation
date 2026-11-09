using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Disclosure.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;

namespace Elsa.Activities.Design.Disclosure.Tasks;

/// <summary>Sequential read-back initialization; no scoped contributor survives this task.</summary>
internal sealed class InitializeCatalogDisclosure(
    IInlineEventPublisher events,
    CatalogDisclosureRegistry registry,
    CatalogDisclosureComposition composition) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            composition.ValidateReplacements();
            var initialization = new ActivityCatalogDisclosureInitializing();
            await events.Publish(initialization, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var registrations = initialization.Registrations.ToArray();
            composition.ValidateContributions(registrations);
            registry.Initialize(registrations);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CatalogDisclosureUnavailableException) { throw; }
        catch (Exception error) { throw new CatalogDisclosureUnavailableException(error); }
    }
}
