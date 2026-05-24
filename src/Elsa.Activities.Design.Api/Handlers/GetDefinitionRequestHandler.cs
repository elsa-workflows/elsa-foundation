using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using System.Linq.Expressions;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(IQueries<ActivityDefinitionVersion> versionQueries, IQueries<ActivityDefinition> defQueries, IObjectMapper mapper) 
    : IRequestHandler<GetDefinition, ActivityDefinitionDetailsView>
{
    public async Task<ActivityDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var definitionTask = defQueries.Get(request.Id, cancellationToken);
        var filter = new ActivityDefinitionVersionFilter { DefinitionId = request.Id };
        var versionsTask = versionQueries.Query(filter, Expressions.VersionViewSelector, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;

        return new ActivityDefinitionDetailsView(
            mapper.Map<ActivityDefinitionView>(definition),
            versions
        );
    }    
}
