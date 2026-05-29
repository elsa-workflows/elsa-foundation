using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(a). Branch coverage per framework §2.23.2 — orphan, start excluded, connected.
/// </summary>
public sealed class OrphanActivityValidatorTests
{
    private static OrphanActivityValidator Validator() => new();

    [Fact]
    public async Task Orphan_activity_emits_error()
    {
        var state = State(activities: [Node("orphan")]);
        var evt = EventFor(state);

        await Validator().Handle(evt, CancellationToken.None);

        var error = Assert.Single(evt.Errors);
        Assert.Equal("orphan", error.Path);
        Assert.Equal("Graph/OrphanActivity", error.Type);
    }

    [Fact]
    public async Task Activity_with_only_inbound_does_not_emit_error()
    {
        var state = State(
            activities: [Node("a"), Node("b")],
            connections: [Edge("a", "b")]
        );
        var evt = EventFor(state);

        await Validator().Handle(evt, CancellationToken.None);

        Assert.DoesNotContain(evt.Errors, e => e.Path == "b");
    }

    [Fact]
    public async Task Activity_with_only_outbound_does_not_emit_error()
    {
        var state = State(
            activities: [Node("a"), Node("b")],
            connections: [Edge("a", "b")]
        );
        var evt = EventFor(state);

        await Validator().Handle(evt, CancellationToken.None);

        Assert.DoesNotContain(evt.Errors, e => e.Path == "a");
    }

    [Fact]
    public async Task IsStart_activity_is_excluded_even_when_disconnected()
    {
        var state = State(activities: [Node("start", isStart: true)]);
        var evt = EventFor(state);

        await Validator().Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Errors);
    }
}
