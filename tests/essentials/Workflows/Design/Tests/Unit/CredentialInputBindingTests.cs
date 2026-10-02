using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// The acceptance predicate of the credential-literal rule (spec 188, FR-008, FR-009), one row of the contract's table
/// (contracts/credential-literal-rule.md) per case: a credential input accepts no binding, a binding that carries no
/// value, and a secret reference; it refuses every literal, object, default request, value read and expression. An input
/// that is not a credential accepts everything.
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
    [MemberData(nameof(Refused))]
    public void Every_other_binding_of_a_credential_input_is_refused(string row, ArgumentState state) =>
        Assert.False(CredentialInputBinding.IsAccepted(isCredential: true, state), row);

    [Theory]
    [MemberData(nameof(Refused))]
    public void An_input_that_is_not_a_credential_accepts_every_binding(string row, ArgumentState state) =>
        Assert.True(CredentialInputBinding.IsAccepted(isCredential: false, state), row);

    private static ArgumentState Bound(object? value, string expressionType) =>
        new(Key, new ArgumentValue(value, expressionType), null, null, null, null);
}
