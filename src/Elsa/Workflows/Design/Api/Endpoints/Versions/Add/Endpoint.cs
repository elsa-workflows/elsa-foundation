using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions.Add;

[Post("versions/ingest")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IAddWorkflowDefinitionVersionCommand addCommand,
    IWorkflowDefinitionVersionStore versionStore) : ApiEndpoint<AddVersion, WorkflowDefinitionVersionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsAdd";
        options.Accepts = ["application/json"];
    }

    public override async Task<WorkflowDefinitionVersionDetailsView> HandleAsync(AddVersion command, CancellationToken cancellationToken)
    {
        var operationKey = DesignOperationKey.CreateOrGenerate(command.OperationKey);
        var result = await addCommand.Execute(
            operationKey,
            command.DefinitionId,
            command.State.ToState(),
            cancellationToken);
        var addedVersion = await versionStore.GetWithDefinitionAsync(result.VersionId, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
