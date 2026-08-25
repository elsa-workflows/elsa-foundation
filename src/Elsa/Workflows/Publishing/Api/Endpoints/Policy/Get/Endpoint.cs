using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Policy.Get;

[Get("/publishing/workflows/{definitionId}/policy")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(
    IPublicationPolicyStore policyStore,
    TimeProvider timeProvider) : ApiEndpoint<GetWorkflowPublicationPolicy, PublicationPolicyView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetWorkflowPublicationPolicyEndpoint";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<PublicationPolicyView> HandleAsync(GetWorkflowPublicationPolicy request, CancellationToken cancellationToken)
    {
        var policy = await policyStore.FindAsync(request.DefinitionId, cancellationToken);
        if (policy is not null)
            return PublicationPolicyView.From(request.DefinitionId, policy, PublicationPolicySource.Workflow);

        var hostPolicy = await policyStore.FindAsync(null, cancellationToken)
            ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, timeProvider.GetUtcNow());
        return PublicationPolicyView.From(request.DefinitionId, hostPolicy, PublicationPolicySource.Host);
    }
}
