using System.Text.Json.Nodes;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetWorkflowDefinitionSubmitSchemaHandler
    : IRequestHandler<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>
{
    /// <summary>Version of the published submit-schema document contract.</summary>
    internal const string SchemaVersion = "1";

    public Task<WorkflowDefinitionSubmitSchemaView> Handle(
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
