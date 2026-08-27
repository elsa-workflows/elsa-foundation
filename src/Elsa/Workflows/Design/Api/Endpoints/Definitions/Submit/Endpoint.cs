using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;

[Post("definitions/submit")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    ISubmitWorkflowDefinitionCommand submitCommand,
    IWorkflowDefinitionVersionStore versionStore) : ApiEndpoint<SubmitDefinition, SubmittedWorkflowDefinitionView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsSubmit";
        options.Accepts = ["application/json"];
    }

    public override async Task<SubmittedWorkflowDefinitionView> HandleAsync(SubmitDefinition command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.State);

        var submitted = await submitCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.Name,
            command.Description,
            command.State.ToState(),
            cancellationToken);

        var version = await versionStore.GetWithDefinitionAsync(submitted.VersionId, cancellationToken);
        var versionView = version.ToDetailsView();

        return new SubmittedWorkflowDefinitionView(
            versionView.Definition,
            submitted.DraftId,
            versionView);
    }
}
