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

public sealed class ListDefinitionVersionsRequestHandler(IQueries<ActivityDefinitionVersion> queries)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<ActivityDefinitionVersionView>>
{
    public async Task<IEnumerable<ActivityDefinitionVersionView>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter { DefinitionId = request.DefinitionId };
        return await queries.Query(filter, Expressions.VersionViewSelector, cancellationToken);
    }

    
}
