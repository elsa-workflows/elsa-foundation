using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListActivityAvailabilityDiagnosticsRequestHandler(
    IActivityDefinitionStore definitionStore,
    IActivityAvailabilitySettingsStore settingsStore,
    IActivityAvailabilityDiagnosticsProjector diagnosticsProjector,
    IOptions<ActivityAvailabilityOptions> options)
    : IRequestHandler<ListActivityAvailabilityDiagnostics, ActivityAvailabilityDiagnostics>
{
    public async Task<ActivityAvailabilityDiagnostics> Handle(ListActivityAvailabilityDiagnostics request, CancellationToken cancellationToken)
    {
        var definitions = await definitionStore.ListAsync(new ActivityDefinitionFilter(), cancellationToken);
        var settings = await settingsStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);

        return diagnosticsProjector.Project(definitions, options.Value, settings);
    }
}
