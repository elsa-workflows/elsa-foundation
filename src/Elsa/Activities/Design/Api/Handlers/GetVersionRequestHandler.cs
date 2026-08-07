using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetVersionRequestHandler(
    IActivityDefinitionVersionStore versionStore,
    IActivityFeatureAttributionResolver featureAttributionResolver) : IRequestHandler<GetVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        var result = await versionStore.GetWithDefinitionAsync(request.VersionId, cancellationToken);

        // Feature attribution only applies to CLR-backed versions (the activity type key is a well-known
        // type alias for those); other consumers keep source provenance with a null feature id.
        var featureId = string.Equals(result.ConsumerKey, ActivityProvenanceConstants.ClrActivityConsumerKey, StringComparison.Ordinal)
            ? await featureAttributionResolver.ResolveProvidingFeatureAsync(result.Definition!.ActivityTypeKey, cancellationToken)
            : null;

        return result.ToDetailsView(featureId);
    }
}
