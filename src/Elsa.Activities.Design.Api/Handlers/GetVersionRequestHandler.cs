using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetVersionRequestHandler(IQueries<ActivityDefinitionVersion> queries, IObjectMapper mapper) : IRequestHandler<GetVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        var result = await queries.GetVersionInlcudingDefinition(request.VersionId, cancellationToken);
        return mapper.Map<ActivityDefinitionVersionDetailsView>(result);
    }
}
