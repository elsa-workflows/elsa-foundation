using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Policy.Set;

[Put("/publishing/workflows/{definitionId}/policy")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(
    IPublicationPolicyStore policyStore,
    TimeProvider timeProvider) : ApiEndpoint<SetWorkflowPublicationPolicy, PublicationPolicyView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "SetWorkflowPublicationPolicyEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override async Task<PublicationPolicyView> HandleAsync(SetWorkflowPublicationPolicy request, CancellationToken cancellationToken)
    {
        var policy = new PublicationPolicy(
            request.DefinitionId,
            PublicationPolicyContract.ToModel(request.DefaultAction),
            request.DefaultSlotName,
            request.ExpectedRevision,
            timeProvider.GetUtcNow());
        var result = await policyStore.TrySaveAsync(policy, request.ExpectedRevision, cancellationToken);
        if (!result.Succeeded)
            throw new PublicationPolicyRevisionConflictException();

        return PublicationPolicyView.From(request.DefinitionId, result.Policy, PublicationPolicySource.Workflow);
    }
}
