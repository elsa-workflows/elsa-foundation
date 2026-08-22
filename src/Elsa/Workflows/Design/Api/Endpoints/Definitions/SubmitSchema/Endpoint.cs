using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SubmitSchema;

[Get("definitions/submit/schema")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : RequestEndpoint<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "DefinitionsSubmitSchema";
}
