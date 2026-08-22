using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;

[Post("definitions/submit")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<SubmitDefinition, SubmittedWorkflowDefinitionView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsSubmit";
        options.Accepts = ["application/json"];
    }
}
