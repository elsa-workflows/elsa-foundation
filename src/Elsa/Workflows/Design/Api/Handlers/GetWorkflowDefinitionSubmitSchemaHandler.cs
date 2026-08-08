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
        ConstrainIntrinsicsByKind(node);
        var schema = AuthoringSchemaExporter.ToElement(node);
        var fingerprint = AuthoringSchemaExporter.ComputeFingerprint(schema);
        return Task.FromResult(new WorkflowDefinitionSubmitSchemaView(SchemaVersion, fingerprint, schema));
    }

    // Kinds whose intrinsic carries a portable value type, and kinds that write a variable target.
    // These mirror AuthoredWorkflowIntrinsic's constructor invariants exactly; they are conditional on
    // `kind`, which a plain type export cannot express — so the exported schema accepted bodies the
    // server rejects ("Authored workflow intrinsic 'Set' requires a portable value type").
    private static readonly string[] KindsWithoutValueType = ["control", "finish"];
    private static readonly string[] KindsWritingAVariable = ["set", "merge", "reduce"];

    /// <summary>
    /// Layers <see cref="AuthoredWorkflowIntrinsic"/>'s kind-dependent invariants onto the exported
    /// schema so a schema-validating client cannot produce a body the runtime refuses: every kind except
    /// <c>control</c>/<c>finish</c> requires <c>valueType</c>, and only <c>set</c>/<c>merge</c>/<c>reduce</c>
    /// may (and must) carry <c>variable</c>.
    /// </summary>
    private static void ConstrainIntrinsicsByKind(JsonNode schema)
    {
        var intrinsic = FindIntrinsicSchema(schema);
        if (intrinsic is null)
            return;

        var conditions = new JsonArray
        {
            RequireWhenKindIsNot(KindsWithoutValueType, "valueType"),
            ForbidWhenKindIsIn(KindsWithoutValueType, "valueType"),
            RequireWhenKindIsIn(KindsWritingAVariable, "variable"),
            ForbidWhenKindIsNot(KindsWritingAVariable, "variable")
        };

        intrinsic["allOf"] = conditions;
    }

    private static JsonObject RequireWhenKindIsIn(string[] kinds, string member) =>
        new()
        {
            ["if"] = KindMatches(kinds, negate: false),
            ["then"] = new JsonObject { ["required"] = new JsonArray(member) }
        };

    private static JsonObject RequireWhenKindIsNot(string[] kinds, string member) =>
        new()
        {
            ["if"] = KindMatches(kinds, negate: true),
            ["then"] = new JsonObject { ["required"] = new JsonArray(member) }
        };

    private static JsonObject ForbidWhenKindIsIn(string[] kinds, string member) =>
        new()
        {
            ["if"] = KindMatches(kinds, negate: false),
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject { [member] = new JsonObject { ["type"] = "null" } }
            }
        };

    private static JsonObject ForbidWhenKindIsNot(string[] kinds, string member) =>
        new()
        {
            ["if"] = KindMatches(kinds, negate: true),
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject { [member] = new JsonObject { ["type"] = "null" } }
            }
        };

    private static JsonObject KindMatches(string[] kinds, bool negate)
    {
        var match = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject { ["enum"] = new JsonArray(kinds.Select(kind => (JsonNode?)kind).ToArray()) }
            },
            ["required"] = new JsonArray("kind")
        };

        return negate ? new JsonObject { ["not"] = match } : match;
    }

    /// <summary>Locates the intrinsic sub-schema wherever the exporter placed it (inline or under $defs).</summary>
    private static JsonObject? FindIntrinsicSchema(JsonNode schema)
    {
        var queue = new Queue<JsonNode?>();
        queue.Enqueue(schema);
        while (queue.Count > 0)
        {
            switch (queue.Dequeue())
            {
                case JsonObject candidate:
                    if (candidate["properties"] is JsonObject properties)
                    {
                        if (properties["intrinsic"] is JsonObject intrinsic && intrinsic["properties"]?["kind"] is not null)
                            return intrinsic;
                        foreach (var property in properties)
                            queue.Enqueue(property.Value);
                    }

                    foreach (var member in candidate)
                    {
                        if (member.Key != "properties")
                            queue.Enqueue(member.Value);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                        queue.Enqueue(item);
                    break;
            }
        }

        return null;
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
