using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowExecutableInputValidatorTests
{
    [Fact]
    public void Complete_bag_materializes_optional_null_and_scalar_type_defaults()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string)), ("Boolean", typeof(bool)), ("Int32", typeof(int))));
        var result = validator.Validate(Contract(
            new("text", new TypeReference("String"), false, isNullable: true),
            new("flag", new TypeReference("Boolean"), false),
            new("count", new TypeReference("Int32"), false)), []);

        Assert.True(result.IsValid);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.NormalizedInputs["text"].ValueKind);
        Assert.False(result.NormalizedInputs["flag"].GetBoolean());
        Assert.Equal(0, result.NormalizedInputs["count"].GetInt32());
    }

    [Fact]
    public void Known_inputs_add_only_declared_defaults_and_leave_required_values_unresolved()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string)), ("Int32", typeof(int))));
        var result = validator.ValidateKnownInputs(Contract(
            new("required", new TypeReference("Int32"), true),
            new("text", new TypeReference("String"), false, isNullable: true),
            new("count", new TypeReference("Int32"), false),
            new("defaulted", new TypeReference("Int32"), false, Json("7"))), [], new(DefaultOnNull: true));

        Assert.True(result.IsValid);
        Assert.Equal(7, Assert.Single(result.NormalizedInputs).Value.GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_on_null_applies_after_explicit_null_default_completion_in_both_modes(bool knownInputs)
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var contract = Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), false, Json("null")));
        var result = knownInputs
            ? validator.ValidateKnownInputs(contract, [], new(DefaultOnNull: true))
            : validator.Validate(contract, [], new(DefaultOnNull: true));
        Assert.True(result.IsValid);
        Assert.Equal(0, result.NormalizedInputs["count"].GetInt32());

        var refused = knownInputs ? validator.ValidateKnownInputs(contract, []) : validator.Validate(contract, []);
        Assert.False(refused.IsValid);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.NullNotAllowed, Assert.Single(refused.Findings).ReasonCode);
    }

    [Fact]
    public void Requiredness_precedes_declared_defaults_even_under_policy()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), true, Json("9"))), [], new(DefaultOnNull: true));
        Assert.False(result.IsValid);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.MissingRequired, Assert.Single(result.Findings).ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplied_nullable_null_satisfies_presence_and_does_not_use_a_default(bool required)
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(Contract(new WorkflowDeclaredInput("text", new TypeReference("String"), required, Json("\"fallback\""), true)),
            [Pair("text", Json("null"))], new(DefaultOnNull: true));
        Assert.True(result.IsValid);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.NormalizedInputs["text"].ValueKind);
    }

    [Fact]
    public void Policy_does_not_fabricate_a_date_or_non_nullable_string()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("DateTime", typeof(DateTime)), ("String", typeof(string))));
        foreach (var alias in new[] { "DateTime", "String" })
        {
            var result = validator.Validate(Contract(new WorkflowDeclaredInput("value", new TypeReference(alias), false)),
                [Pair("value", Json("null"))], new(DefaultOnNull: true));
            Assert.False(result.IsValid);
            Assert.Equal(WorkflowExecutableInputValidationReasonCodes.NullNotAllowed, Assert.Single(result.Findings).ReasonCode);
        }
    }
}
