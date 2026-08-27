using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using NativeEndpoints;
using System.Text.Json.Nodes;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SubmitSchema;

[Get("definitions/submit/schema")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "DefinitionsSubmitSchema";

    /// <summary>Version of the published submit-schema document contract.</summary>
    internal const string SchemaVersion = "1";

    public override Task<WorkflowDefinitionSubmitSchemaView> HandleAsync(
        GetWorkflowDefinitionSubmitSchema request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Opaque members of the submit graph (ArgumentValue.Value, structure payloads) deliberately
        // export as unconstrained schemas; the structure registry endpoint documents payloads per kind.
        var node = AuthoringSchemaExporter.ExportSchemaNode(typeof(SubmitDefinition));
        RequireSubmitRootActivity(node);
        var schema = AuthoringSchemaExporter.ToElement(node);
        var fingerprint = AuthoringSchemaExporter.ComputeFingerprint(schema);
        return Task.FromResult(new WorkflowDefinitionSubmitSchemaView(SchemaVersion, fingerprint, schema));
    }

    // The state view declares rootActivity as nullable because blank definitions are legal on the
    // add/replace operations that share the type. The submit operation always rejects a missing
    // root activity (SubmittedActivityTreeValidator, HTTP 400), so this operation's schema must
    // declare it required.
    private static void RequireSubmitRootActivity(JsonNode schema)
    {
        if (schema["properties"]?["state"] is not JsonObject state)
            throw new InvalidOperationException("Submit schema does not expose a 'state' object to constrain.");

        if (state["required"] is not JsonArray required)
            state["required"] = required = [];

        if (!required.Any(member => member?.GetValue<string>() == "rootActivity"))
            required.Add("rootActivity");
    }
}
