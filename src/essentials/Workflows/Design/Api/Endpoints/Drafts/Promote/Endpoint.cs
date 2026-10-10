using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Promote;

/// <summary>
/// Promotes a draft to a new version. The draft is read and admitted here (the credential-literal rule, spec 188
/// FR-008), and the promotion command is handed the hash of exactly that draft, so it promotes only the content admitted
/// here and refuses with a conflict when the draft changed in between (research R7).
/// </summary>
[Post("drafts/{draftId}/promote")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IWorkflowDefinitionDraftStore draftStore,
    IPromoteDraftToVersionCommand promoteCommand,
    IWorkflowVersionDetailsReader versionReader,
    ICredentialLiteralValidator credentialLiterals) : ApiEndpoint<PromoteDraft, WorkflowDefinitionVersionDetailsView>
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
        // A missing draft is not refused here: the command resolves a replay of an already-succeeded promotion before it
        // reads the draft, so a replay after the draft was discarded still returns the original version, and a first
        // promotion of a missing draft is refused by the command's own lookup (404). Nothing was admitted then, so the
        // command is handed WorkflowDraftStateHash.Absent: a draft that appears in between differs from it and is
        // refused as changed, unless its stored state source is null or empty, which holds no state to promote and which
        // the EF command refuses to read before it writes anything.
        var draft = await draftStore.FindByIdAsync(command.DraftId, cancellationToken);
        var admittedStateHash = WorkflowDraftStateHash.Absent;
        if (draft is not null)
        {
            await credentialLiterals.AdmitAsync(draft.State, cancellationToken);
            admittedStateHash = WorkflowDraftStateHash.Compute(draft.StateSource);
        }

        var versionId = await promoteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            command.RequestedVersion,
            admittedStateHash,
            cancellationToken);
        return await versionReader.ReadAsync(versionId, cancellationToken);
    }
}
