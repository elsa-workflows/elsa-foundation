using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(c). Branch coverage — distinct names, case-insensitive collisions, multi-collisions.
/// </summary>
public sealed class VariableUniquenessValidatorTests
{
    private static VariableUniquenessValidator Validator() => new(Options(), Walker(), StructureService());

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

    [Fact]
    public async Task Duplicate_names_within_one_container_scope_emit_an_error()
    {
        // #972 / ADR 0027: uniqueness is per declaring scope — a container's own bag must not collide.
        var container = Node(
            "container-1",
            containerVariables: [Variable("v1", "Local"), Variable("v2", "local")]);
        var state = StateWithRoot(container);

        var error = Assert.Single(await Validate(Validator(), state));
        Assert.Equal("Variables/Uniqueness", error.Type);
        Assert.StartsWith("container-1/variables/", error.Path);
        Assert.Contains("container 'container-1'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_name_in_different_scopes_is_allowed_shadowing()
    {
        // ADR 0027: nested scopes may shadow; only within-scope duplicates are errors.
        var container = Node("container-1", containerVariables: [Variable("v-inner", "Shared")]);
        var root = Node("root", childActivities: [container], containerVariables: [Variable("v-outer", "Shared")]);
        var state = StateWithRoot(root, variables: [Variable("v-workflow", "Shared")]);

        Assert.Empty(await Validate(Validator(), state));
    }
}
