using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(IActivityDefinitionVersionStore versionStore, IActivityDefinitionStore definitionStore)
    : IRequestHandler<GetDefinition, ActivityDefinitionDetailsView>
{
    public async Task<ActivityDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var definitionTask = definitionStore.GetAsync(request.Id, cancellationToken);
        var versionsTask = versionStore.ListByDefinitionAsync(request.Id, cancellationToken);

        var definition = await definitionTask;
        var versionRows = await versionsTask;
        var versions = versionRows.Select(e => new ActivityDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt, e.ExecutionType)).ToArray();

        return new ActivityDefinitionDetailsView(definition.ToView(), versions);
    }
}
