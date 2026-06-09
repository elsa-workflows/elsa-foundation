using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListDefinitionVersionsRequestHandler(IQueries<ActivityDefinitionVersion> queries)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<ActivityDefinitionVersionInfo>>
{
    public async Task<IEnumerable<ActivityDefinitionVersionInfo>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter { DefinitionId = request.DefinitionId };
        return await queries.Query(filter, Expressions.VersionInfoSelector, cancellationToken);
    }


}
