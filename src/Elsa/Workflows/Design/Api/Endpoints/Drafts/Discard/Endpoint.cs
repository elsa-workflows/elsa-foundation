using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Discard;

[Delete("drafts/{draftId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(IDiscardDraftCommand discardCommand) : ApiEndpoint<DiscardDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsDiscard";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task HandleAsync(DiscardDraft command, CancellationToken cancellationToken) =>
        discardCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            cancellationToken);
}
