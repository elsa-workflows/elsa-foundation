using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetActivityDependenciesHandler(ActivityDependencyReader reader)
    : IRequestHandler<GetActivityDependencies, ActivityDependencyPageView>
{
    public Task<ActivityDependencyPageView> Handle(GetActivityDependencies request, CancellationToken cancellationToken) =>
        reader.ReadAsync(
            request.VersionId,
            request.Direction,
            request.Transitive,
            request.Include,
            request.Cursor,
            request.Limit,
            cancellationToken);
}
