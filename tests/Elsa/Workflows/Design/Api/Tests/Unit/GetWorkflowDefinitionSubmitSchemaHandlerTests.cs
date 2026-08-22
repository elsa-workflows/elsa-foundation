using System.Text.Json;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class GetWorkflowDefinitionSubmitSchemaHandlerTests
{
    private static Task<Models.WorkflowDefinitionSubmitSchemaView> HandleAsync() =>
        new GetWorkflowDefinitionSubmitSchemaHandler().Handle(new GetWorkflowDefinitionSubmitSchema(), CancellationToken.None);

    [Fact]
    public async Task Schema_document_is_versioned_and_stably_fingerprinted()
    {
        var first = await HandleAsync();
        var second = await HandleAsync();

        Assert.Equal("1", first.SchemaVersion);
        Assert.StartsWith("sha256:", first.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public async Task Schema_exposes_the_camel_case_submit_body_contract()
    {
        var view = await HandleAsync();

        var properties = view.Schema.GetProperty("properties");
        AssertProperties(properties, "operationKey", "name", "description", "state");

        var state = properties.GetProperty("state").GetProperty("properties");
        AssertProperties(state, "variables", "rootActivity", "inputs", "outputs", "strategyOptions");
    }

    [Fact]
    public async Task Required_lists_only_members_the_server_rejects_when_absent()
    {
        var view = await HandleAsync();

        // The wire deserializer treats a missing nullable member exactly like an explicit null, so
        // nullable members (operationKey, description, ...) must not be marked required: a client
        // validating against the schema would otherwise reject bodies the server accepts.
        Assert.Equal(["name", "state"], GetRequired(view.Schema));

        var argumentState = GetRootActivitySchema(view.Schema).GetProperty("properties").GetProperty("inputs").GetProperty("items");
        Assert.Equal(["referenceKey", "value"], GetRequired(argumentState));

        // The converse also holds: the submit operation always rejects a missing root activity
        // (SubmittedActivityTreeValidator → 400) even though the shared state view type is nullable
        // for add/replace, so this operation's schema must require it.
        Assert.Equal(["rootActivity"], GetRequired(view.Schema.GetProperty("properties").GetProperty("state")));
    }

    private static string[] GetRequired(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(value => value.GetString()!).ToArray()
            : [];

    [Fact]
    public async Task Argument_state_schema_documents_the_authored_wire_contract()
    {
        var argumentState = await GetInputArgumentStateSchemaAsync();

        var properties = argumentState.GetProperty("properties");
        AssertProperties(
            properties,
            "referenceKey", "value", "autoEvaluate", "evaluatorType", "storageDriverType", "isSensitive", "conversion");

        // ArgumentValue.Value is object? — deliberately opaque, so it exports as the
        // unconstrained schema (JSON `true`), accepting any payload.
        var authoredValue = properties.GetProperty("value").GetProperty("properties").GetProperty("value");
        Assert.Equal(JsonValueKind.True, authoredValue.ValueKind);
    }

    [Fact]
    public async Task Enums_appear_as_camel_case_string_enums()
    {
        var argumentState = await GetInputArgumentStateSchemaAsync();

        var modes = argumentState
            .GetProperty("properties").GetProperty("conversion")
            .GetProperty("properties").GetProperty("mode")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Equal(["auto", "none", "json", "xml", "profile"], modes);
    }

    [Fact]
    public async Task Output_argument_states_reuse_the_input_item_schema_by_pointer_reference()
    {
        var view = await HandleAsync();

        // The exporter inlines the graph and deduplicates the second ArgumentState occurrence
        // with a JSON-pointer $ref into the document (no $defs section is emitted).
        var reference = GetRootActivitySchema(view.Schema)
            .GetProperty("properties").GetProperty("outputs")
            .GetProperty("items").GetProperty("$ref")
            .GetString();

        Assert.Equal("#/properties/state/properties/rootActivity/properties/inputs/items", reference);
    }

    private static async Task<JsonElement> GetInputArgumentStateSchemaAsync()
    {
        var view = await HandleAsync();
        return GetRootActivitySchema(view.Schema).GetProperty("properties").GetProperty("inputs").GetProperty("items");
    }

    private static JsonElement GetRootActivitySchema(JsonElement schema) =>
        schema.GetProperty("properties").GetProperty("state").GetProperty("properties").GetProperty("rootActivity");

    private static void AssertProperties(JsonElement propertiesSchema, params string[] names) =>
        Assert.All(names, name => Assert.True(
            propertiesSchema.TryGetProperty(name, out _),
            $"Expected schema property '{name}' among: {string.Join(", ", propertiesSchema.EnumerateObject().Select(p => p.Name))}"));
}
