using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(IQueries<ActivityDefinition> queries, IObjectMapper mapper)

    : IRequestHandler<ListDefinitions, IEnumerable<ActivityDefinitionView>>
{
    public async Task<IEnumerable<ActivityDefinitionView>> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionFilter
        {
            Category = request.Category,
            Description = request.Description,
            DisplayName = request.DisplayName,
            Id = request.Id,
            IsBrowsable = request.IsBrowsable,
            SearchTerm = request.SearchTerm,
            TenantAgnostic = request.TenantAgnostic
        };

        var activities = (await queries.Query(filter, cancellationToken)).ToArray();

        var result = new List<ActivityDefinitionView>(activities.Length);
        var enumerable = mapper.Map<ActivityDefinitionView>(activities, cancellationToken);
        await foreach (var item in enumerable)
        {
            result.Add(item);
        }

        return result;
    }
}
