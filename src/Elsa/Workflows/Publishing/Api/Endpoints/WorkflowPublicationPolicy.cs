using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class GetWorkflowPublicationPolicyEndpoint(IPublicationPolicyStore policyStore, TimeProvider timeProvider)
    : ElsaEndpoint<GetWorkflowPublicationPolicy, PublicationPolicyView>
{
    public override void Configure()
    {
        Get(RouteConstants.WorkflowPolicy);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(GetWorkflowPublicationPolicy request, CancellationToken cancellationToken)
    {
        var workflowPolicy = await policyStore.FindAsync(request.DefinitionId, cancellationToken);
        if (workflowPolicy is not null)
        {
            await Send.OkAsync(PublicationPolicyView.From(request.DefinitionId, workflowPolicy, PublicationPolicySource.Workflow), cancellationToken);
            return;
        }

        var hostPolicy = await policyStore.FindAsync(workflowDefinitionId: null, cancellationToken)
            ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, timeProvider.GetUtcNow());
        await Send.OkAsync(PublicationPolicyView.From(request.DefinitionId, hostPolicy, PublicationPolicySource.Host), cancellationToken);
    }
}

internal sealed class SetWorkflowPublicationPolicyEndpoint(IPublicationPolicyStore policyStore, TimeProvider timeProvider)
    : ElsaEndpoint<SetWorkflowPublicationPolicy, PublicationPolicyView>
{
    public override void Configure()
    {
        Put(RouteConstants.WorkflowPolicy);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override async Task HandleAsync(SetWorkflowPublicationPolicy request, CancellationToken cancellationToken)
    {
        var policy = new PublicationPolicy(
            request.DefinitionId,
            PublicationPolicyContract.ToModel(request.DefaultAction),
            request.DefaultSlotName,
            request.ExpectedRevision,
            timeProvider.GetUtcNow());
        var result = await policyStore.TrySaveAsync(policy, request.ExpectedRevision, cancellationToken);
        if (!result.Succeeded)
        {
            ThrowError("The workflow publication policy revision changed.", 409);
            return;
        }

        await Send.OkAsync(PublicationPolicyView.From(request.DefinitionId, result.Policy, PublicationPolicySource.Workflow), cancellationToken);
    }
}
