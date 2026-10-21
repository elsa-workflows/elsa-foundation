using System.Text.Json;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 256 (#3050): Elsa 3 WriteHttpResponse content converts when Elsa 4 sends the same body and content type. Elsa 3
/// sent text as is and serialized any other value as JSON when the content type was unset or exactly a JSON type its
/// writer handled; otherwise it sent the value's type name. Elsa 4's body is any JSON value.
/// </summary>
public sealed class Elsa3HttpResponseBodyTests
{
    private const string ObjectBody = "return { id: 42, items: [1, 2] };";

    // The catalog alias of WriteHttpResponse.Body; publication requires a portable expression to declare it verbatim.
    private const string BodyType = "System.Text.Json.JsonElement?";

    [Fact]
    public async Task Object_content_without_a_content_type_converts_and_declares_the_body_type()
    {
        var state = await ConvertAsync(Respond(("content", Expression("JavaScript", ObjectBody, "Object"))));

        var body = Input(Node(state, "respond"), "Body");
        Assert.Contains("id: 42", Source(body), StringComparison.Ordinal);
        Assert.Equal(BodyType, ResultTypeAlias(body));
    }

    [Theory]
    [InlineData("Literal", "application/json")]
    [InlineData("Literal", "text/json")]
    [InlineData("Literal", "")]
    [InlineData("JavaScript", "")]
    [InlineData("JavaScript", "  ")]
    public async Task Object_content_with_an_unset_or_JSON_content_type_converts(string contentTypeSyntax, string contentType)
    {
        var state = await ConvertAsync(Respond(
            ("content", Expression("JavaScript", ObjectBody, "Object")),
            ("contentType", Expression(contentTypeSyntax, contentType))));

        Assert.Contains("id: 42", Source(Input(Node(state, "respond"), "Body")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_content_type_expression_with_a_blank_script_is_dropped()
    {
        // Elsa 3 evaluated it to null and inferred the type; Elsa 4 infers it too when the input is unset.
        var state = await ConvertAsync(Respond(
            ("content", Expression("JavaScript", ObjectBody, "Object")),
            ("contentType", Expression("JavaScript", string.Empty))));

        Assert.DoesNotContain(Node(state, "respond").Inputs, input => input.ReferenceKey == "ContentType");
    }

    [Fact]
    public async Task Literal_content_is_text_and_converts_with_any_content_type()
    {
        var state = await ConvertAsync(Respond(
            ("content", Literal("""{"id":42}""", "Object")),
            ("contentType", Literal("text/plain"))));

        var node = Node(state, "respond");
        Assert.Equal("""{"id":42}""", Text(Input(node, "Body")));
        Assert.Equal("text/plain", Text(Input(node, "ContentType")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/html")]
    public async Task A_script_declared_String_is_refused(string? contentType)
    {
        // Elsa 3 converted the result to text; the lowered body declares JSON, so Elsa 4 would send an object as JSON.
        (string, JsonElement)[] properties = contentType is null
            ? [("content", Expression("JavaScript", "return 'hello';"))]
            : [("content", Expression("JavaScript", "return 'hello';")), ("contentType", Literal(contentType))];

        var result = await ConvertResultAsync(Respond(properties));

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract);
        Assert.Contains("declared as 'String'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Object", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_String_variable_converts_with_a_non_JSON_content_type()
    {
        // A String variable holds text, which both Elsa 3 and Elsa 4 send as is.
        var content = JsonSerializer.SerializeToElement(new
        {
            typeName = "String",
            expression = new { type = "Variable", value = new { id = "greeting", name = "Greeting", typeName = "String" } }
        });
        var root = Respond(("content", content), ("contentType", Literal("text/html")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root,
            variables: [new Elsa3Variable { Id = "greeting", Name = "Greeting", TypeName = "String" }]));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("Application/JSON")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    public async Task Object_content_with_another_spelling_of_a_JSON_type_converts_with_a_warning(string contentType)
    {
        // Elsa 3 matched its JSON writer by exact string and sent the type name; Elsa 4 sends the JSON.
        var result = await ConvertResultAsync(Respond(
            ("content", Expression("JavaScript", ObjectBody, "Object")),
            ("contentType", Literal(contentType))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.HttpResponseJsonVariant);
    }

    [Fact]
    public async Task Object_content_with_exactly_application_json_converts_without_a_warning()
    {
        var result = await ConvertResultAsync(Respond(
            ("content", Expression("JavaScript", ObjectBody, "Object")),
            ("contentType", Literal("application/json"))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.HttpResponseJsonVariant);
    }

    [Theory]
    [InlineData("Literal", "text/plain")]
    [InlineData("Literal", "application/xml")]
    [InlineData("JavaScript", "return 'application/json';")]
    public async Task Object_content_whose_Elsa3_response_was_not_JSON_is_refused(string contentTypeSyntax, string contentType)
    {
        // Elsa 3 sent the value's type name as text for a non-JSON type, and a computed type depends on the run.
        var result = await ConvertResultAsync(Respond(
            ("content", Expression("JavaScript", ObjectBody, "Object")),
            ("contentType", Expression(contentTypeSyntax, contentType))));

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract);
        Assert.Contains("'content'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("application/json", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Byte[]")]
    [InlineData("Stream")]
    public async Task Binary_content_is_refused(string typeName)
    {
        var result = await ConvertResultAsync(Respond(("content", Expression("JavaScript", "return getBytes();", typeName))));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract &&
            diagnostic.Message.Contains("binary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_expression_bound_to_a_typed_member_keeps_its_declared_type()
    {
        // Only a JSON member takes the member type; a typed member keeps Elsa 3's, so publication still checks it.
        var state = await ConvertAsync(Sequence("root",
            Activity("write", "Elsa.WriteLine", ("text", Expression("JavaScript", "return 'hi';")))));

        Assert.Equal("String", ResultTypeAlias(Input(Node(state, "write"), "text")));
    }

    [Fact]
    public async Task A_typed_expression_bound_to_a_JSON_member_of_any_activity_declares_the_member_type()
    {
        // The rule applies to every activity, not only Http (ForEach.Collection is object?). Elsa 3 converted this value
        // to its declared Int32; Elsa 4 receives what the script returns (here the string "5"), because the member
        // accepts any JSON value. Publication requires the member type verbatim.
        var state = await ConvertAsync(Sequence("root",
            Activity("loop", "Elsa.ForEach", ("items", Expression("JavaScript", "return '5';", "Int32")))));

        Assert.Equal("Object", ResultTypeAlias(Input(Node(state, "loop"), "Collection")));
    }

    private static Elsa3Activity Respond(params (string Name, JsonElement Value)[] properties) =>
        Sequence("root", Activity("respond", "Elsa.WriteHttpResponse", properties));

    private static string? ResultTypeAlias(ArgumentState argument)
    {
        var definition = Assert.IsType<JsonElement>(argument.Value.Value);
        var resultType = definition.GetProperty("resultType");
        return resultType.TryGetProperty("alias", out var alias) ? alias.GetString() : resultType.GetProperty("Alias").GetString();
    }

    private static Task<Elsa3MigrationResult<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition>> ConvertResultAsync(Elsa3Activity root) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

    private static async Task<WorkflowDefinitionState> ConvertAsync(Elsa3Activity root)
    {
        var result = await ConvertResultAsync(root);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return result.Value!.State;
    }
}
