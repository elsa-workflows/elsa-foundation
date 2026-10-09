using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// The acceptance predicate of the credential-literal rule (spec 188, FR-008, FR-009), one row of the contract's table
/// (contracts/credential-literal-rule.md) per case: a credential input accepts no binding, a binding that carries no
/// value, and a secret reference, which is a <c>Secret</c> binding whose payload is a well-formed reference or carries no
/// value; it refuses every literal, object, default request, value read and expression, and a <c>Secret</c> binding with
/// any other payload. An input that is not a credential accepts everything.
/// </summary>
public sealed class CredentialInputBindingTests
{
    private const string Key = "apiKey";

    public static TheoryData<string, ArgumentState?> Unbound => new()
    {
        { "no argument state", null },
        { "null argument value", new(Key, null!, null, null, null, null) },
        { "null literal", Bound(null, "Literal") },
        { "empty string literal", Bound(string.Empty, "Literal") },
        { "JSON null literal", Bound(JsonSerializer.SerializeToElement<object?>(null), "Literal") },
        { "JSON undefined literal", Bound(default(JsonElement), "Literal") },
        { "JSON empty string literal", Bound(JsonSerializer.SerializeToElement(string.Empty), "Literal") }
    };

    public static TheoryData<string> SecretExpressionTypes => new() { "Secret", "secret", "SECRET" };

    /// <summary>The payloads of a <c>Secret</c> binding the rule accepts: a well-formed reference, or no value at all.</summary>
    public static TheoryData<string, object?> AcceptedSecretPayloads => new()
    {
        { "name only", Json(new { name = "reference-name" }) },
        { "name, type and scope", Json(new { name = "reference-name", typeName = "text", scope = "billing" }) },
        { "null type and scope", Json(new { name = "reference-name", typeName = (string?)null, scope = (string?)null }) },
        { "name with surrounding whitespace", Json(new { name = "  reference-name  " }) },
        { "CLR object, not a JSON element", new { name = "reference-name", typeName = "text" } },
        { "no payload", null },
        { "JSON null payload", Json<object?>(null) },
        { "JSON undefined payload", default(JsonElement) },
        { "empty string payload", Json(string.Empty) }
    };

    /// <summary>The payloads of a <c>Secret</c> binding the rule refuses: anything bound that is not a well-formed reference.</summary>
    public static TheoryData<string, object?> RefusedSecretPayloads => new()
    {
        { "text", Json("value") },
        { "whitespace-only text", Json("   ") },
        { "JSON text holding a reference", Json("""{"name":"reference-name"}""") },
        { "number", Json(42) },
        { "boolean", Json(true) },
        { "CLR object without a name, not a JSON element", new { typeName = "text" } },
        { "array", Json(new[] { "value" }) },
        { "object without a name", Json(new { typeName = "text" }) },
        { "blank name", Json(new { name = "   " }) },
        { "non-text name", Json(new { name = 42 }) },
        { "a member outside the reference", Json(new { name = "reference-name", note = "value" }) },
        { "a member twice", JsonDocument.Parse("""{"name":"reference-name","name":"value"}""").RootElement.Clone() },
        { "a member in another case", Json(new { Name = "reference-name" }) },
        { "non-text type", Json(new { name = "reference-name", typeName = 1 }) },
        { "non-text scope", Json(new { name = "reference-name", scope = new { note = "value" } }) }
    };

    public static TheoryData<string, ArgumentState> Refused => new()
    {
        { "Literal", Bound(JsonSerializer.SerializeToElement("value"), "Literal") },
        { "Object", Bound(JsonSerializer.SerializeToElement(new { note = "value" }), "Object") },
        { "Default", Bound(null, "Default") },
        { "Variable", Bound(JsonSerializer.SerializeToElement("variable-reference"), "Variable") },
        { "WorkflowRequest", Bound(JsonSerializer.SerializeToElement(new { memberKey = "member" }), "WorkflowRequest") },
        { "ActivityResult", Bound(JsonSerializer.SerializeToElement(new { nodeId = "producer" }), "ActivityResult") },
        { "JavaScript", Bound(JsonSerializer.SerializeToElement("'value'"), "JavaScript") },
        { "Liquid", Bound(JsonSerializer.SerializeToElement("{{ value }}"), "Liquid") }
    };

    [Theory]
    [MemberData(nameof(Unbound))]
    public void A_binding_that_carries_no_value_leaves_a_credential_input_unbound(string row, ArgumentState? state) =>
        Assert.True(CredentialInputBinding.IsAccepted(isCredential: true, state), row);

    [Theory]
    [MemberData(nameof(SecretExpressionTypes))]
    public void A_secret_reference_is_accepted_whatever_the_case_of_its_expression_type(string expressionType) =>
        Assert.True(CredentialInputBinding.IsAccepted(
            isCredential: true,
            Bound(JsonSerializer.SerializeToElement(new { name = "reference-name" }), expressionType)));

    [Theory]
    [MemberData(nameof(AcceptedSecretPayloads))]
    public void A_secret_binding_whose_payload_is_a_reference_or_carries_no_value_is_accepted(string row, object? payload) =>
        Assert.True(CredentialInputBinding.IsAccepted(isCredential: true, Bound(payload, "Secret")), row);

    [Theory]
    [MemberData(nameof(RefusedSecretPayloads))]
    public void A_secret_binding_with_any_other_payload_is_refused(string row, object? payload) =>
        Assert.False(CredentialInputBinding.IsAccepted(isCredential: true, Bound(payload, "Secret")), row);

    [Theory]
    [MemberData(nameof(RefusedSecretPayloads))]
    public void An_input_that_is_not_a_credential_accepts_every_secret_payload(string row, object? payload) =>
        Assert.True(CredentialInputBinding.IsAccepted(isCredential: false, Bound(payload, "Secret")), row);

    [Theory]
    [MemberData(nameof(Refused))]
    public void Every_other_binding_of_a_credential_input_is_refused(string row, ArgumentState state) =>
        Assert.False(CredentialInputBinding.IsAccepted(isCredential: true, state), row);

    [Theory]
    [MemberData(nameof(Refused))]
    public void An_input_that_is_not_a_credential_accepts_every_binding(string row, ArgumentState state) =>
        Assert.True(CredentialInputBinding.IsAccepted(isCredential: false, state), row);

    private static ArgumentState Bound(object? value, string expressionType) =>
        new(Key, new ArgumentValue(value, expressionType), null, null, null, null);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
