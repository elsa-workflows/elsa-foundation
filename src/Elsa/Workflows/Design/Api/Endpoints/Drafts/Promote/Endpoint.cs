using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Promote;

[Post("drafts/{draftId}/promote")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IPromoteDraftToVersionCommand promoteCommand,
    IWorkflowVersionDetailsReader versionReader) : ApiEndpoint<PromoteDraft, WorkflowDefinitionVersionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsPromote";
        options.Accepts = ["application/json"];
        // The route returns 201, but the published document declares 200. Correcting the
        // document is a contract change, tracked separately from this refactor.
        options.SuccessStatus = StatusCodes.Status201Created;
        options.DocumentedStatus = StatusCodes.Status200OK;
    }

    public override async Task<WorkflowDefinitionVersionDetailsView> HandleAsync(PromoteDraft command, CancellationToken cancellationToken)
    {
        var versionId = await promoteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            command.RequestedVersion,
            cancellationToken);
        return await versionReader.ReadAsync(versionId, cancellationToken);
    }
}
