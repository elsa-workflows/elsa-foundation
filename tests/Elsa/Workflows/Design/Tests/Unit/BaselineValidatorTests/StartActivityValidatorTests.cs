using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(b). Branch coverage per framework §2.23.2 — zero, one, two start activities.
/// </summary>
public sealed class StartActivityValidatorTests
{
    private static StartActivityValidator Validator() => new();

    [Fact]
    public async Task Zero_start_activities_emits_error()
    {
        var state = State(activities: [Node("a"), Node("b")]);
        var errors = await Validate(Validator(), state);

        var error = Assert.Single(errors);
        Assert.Equal("$workflow", error.Path);
        Assert.Equal("Graph/StartActivity", error.Type);
        Assert.Contains("no start", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exactly_one_start_activity_emits_no_error()
    {
        var state = State(activities: [Node("a", isStart: true), Node("b")]);
        var errors = await Validate(Validator(), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Two_start_activities_emits_error()
    {
        var state = State(activities: [Node("a", isStart: true), Node("b", isStart: true)]);
        var errors = await Validate(Validator(), state);

        var error = Assert.Single(errors);
        Assert.Equal("$workflow", error.Path);
        Assert.Equal("Graph/StartActivity", error.Type);
        Assert.Contains("2", error.Message);
    }
}
