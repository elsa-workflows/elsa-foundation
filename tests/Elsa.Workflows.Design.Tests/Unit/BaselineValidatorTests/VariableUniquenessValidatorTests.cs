using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(c). Branch coverage — distinct names, case-insensitive collisions, multi-collisions.
/// </summary>
public sealed class VariableUniquenessValidatorTests
{
    private static VariableUniquenessValidator Validator() => new();

    [Fact]
    public async Task Distinct_variable_names_emit_no_error()
    {
        var state = State(variables: [Variable("v1", "MyVar"), Variable("v2", "OtherVar")]);
        var errors = await Validate(Validator(), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Case_insensitive_collision_emits_one_error_per_group()
    {
        var state = State(variables: [Variable("v1", "MyVar"), Variable("v2", "myvar")]);
        var errors = await Validate(Validator(), state);

        var error = Assert.Single(errors);
        Assert.Equal("Variables/Uniqueness", error.Type);
        Assert.StartsWith("$workflow/variables/", error.Path);
    }

    [Fact]
    public async Task Three_collisions_still_emit_one_error()
    {
        var state = State(variables: [
            Variable("v1", "X"),
            Variable("v2", "X"),
            Variable("v3", "x")
        ]);
        var errors = await Validate(Validator(), state);

        var error = Assert.Single(errors);
        Assert.Contains("3 times", error.Message);
    }
}
