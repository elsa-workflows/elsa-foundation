using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Activities.Construct;

/// <summary>
/// Projects the persisted authored contract used to configure an activity node. Design tooling never
/// constructs a runtime activity object; activation is reserved for a pinned invocation attempt.
/// </summary>
[Get("/publishing/activities/{activityId}/construct")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IActivityDefinitionVersionStore versions) : ApiEndpoint<ConstructActivity, ConstructedActivityView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "Construct";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<ConstructedActivityView> HandleAsync(ConstructActivity request, CancellationToken cancellationToken)
    {
        var version = await versions.GetWithDefinitionAsync(request.ActivityId, cancellationToken);
        return new ConstructedActivityView(
            version.Id,
            version.DescriptorType,
            version.DescriptorPayload,
            version.Inputs.Select(input => new ArgumentView(input.ReferenceKey, input.Name, input.Type.Alias)).ToArray(),
            version.Outputs.Select(output => new ArgumentView(output.ReferenceKey, output.Name, output.Type.Alias)).ToArray());
    }
}
