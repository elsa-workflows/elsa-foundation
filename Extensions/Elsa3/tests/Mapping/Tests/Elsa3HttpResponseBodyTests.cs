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

    [Fact]
    public async Task String_content_converts_with_a_non_JSON_content_type()
    {
        var state = await ConvertAsync(Respond(
            ("content", Expression("JavaScript", "return 'hello';")),
            ("contentType", Literal("text/html"))));

        var body = Input(Node(state, "respond"), "Body");
        Assert.Contains("hello", Source(body), StringComparison.Ordinal);
        Assert.Equal(BodyType, ResultTypeAlias(body));
    }

    [Theory]
    [InlineData("Literal", "text/plain")]
    [InlineData("Literal", "application/problem+json")]
    [InlineData("Literal", "Application/JSON")]
    [InlineData("Literal", "application/json; charset=utf-8")]
    [InlineData("JavaScript", "return 'application/json';")]
    public async Task Object_content_whose_Elsa3_response_was_not_JSON_is_refused(string contentTypeSyntax, string contentType)
    {
        // Elsa 3 matched its JSON writer by exact media type; anything else sent the value's type name as text.
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
