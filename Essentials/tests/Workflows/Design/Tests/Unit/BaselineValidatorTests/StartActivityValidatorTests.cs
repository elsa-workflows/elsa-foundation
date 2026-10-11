using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(b). Branch coverage per framework §2.23.2 — missing and present root activity.
/// </summary>
public sealed class StartActivityValidatorTests
{
    private static StartActivityValidator Validator() => new();

    [Fact]
    public async Task Missing_root_activity_emits_error()
    {
        var state = State();
        var errors = await Validate(Validator(), state);

        var error = Assert.Single(errors);
        Assert.Equal("$workflow", error.Path);
        Assert.Equal("RootActivity/Missing", error.Type);
        Assert.Contains("root activity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Present_root_activity_emits_no_error()
    {
        var state = State(activities: [Node("a")]);
        var errors = await Validate(Validator(), state);

        Assert.Empty(errors);
    }
}
