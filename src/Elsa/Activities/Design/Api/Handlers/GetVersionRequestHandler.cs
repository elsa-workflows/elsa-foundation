using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetVersionRequestHandler(IActivityDefinitionVersionStore versionStore) : IRequestHandler<GetVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        var result = await versionStore.GetWithDefinitionAsync(request.VersionId, cancellationToken);
        return result.ToDetailsView();
    }
}
