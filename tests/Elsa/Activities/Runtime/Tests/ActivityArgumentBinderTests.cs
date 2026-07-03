using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Branch coverage for the feature-internal <see cref="ActivityArgumentBinder"/> (spec 006 T023 / FR-011):
/// match-and-set, the widening rewrap (#313), and the three throw paths (missing property, incompatible
/// argument type, no public setter). Colocated with the other CLR-construction tests (which already
/// reference <c>Elsa.Activities.Primitives</c>) rather than a separate Primitives test project.
/// </summary>
public sealed class ActivityArgumentBinderTests
{
    private readonly ActivityArgumentBinder _binder = new();

    [Fact]
    public void Bind_MatchingNameAndAssignableType_SetsTheProperty()
    {
        var activity = new SingleInputActivity();
        var argument = new InputArgument<string>(new MemoryBlockReference());

        _binder.Bind(activity, Inputs(("Text", argument)), null);

        Assert.Same(argument, activity.Text);
    }

    [Fact]
    public void Bind_MatchesPropertyNameCaseInsensitively()
    {
        var activity = new SingleInputActivity();
        var argument = new InputArgument<string>(new MemoryBlockReference());

        _binder.Bind(activity, Inputs(("text", argument)), null);

        Assert.Same(argument, activity.Text);
    }

    [Fact]
    public void Bind_NoPropertyWithThatName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _binder.Bind(new SingleInputActivity(), Inputs(("Missing", new InputArgument<string>(new MemoryBlockReference()))), null));

        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void Bind_ArgumentTypeNotAssignableAndNotWidenable_Throws()
    {
        // Number is InputArgument<int>; an InputArgument<string> is neither assignable nor a valid widening.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _binder.Bind(new TypedInputActivity(), Inputs(("Number", new InputArgument<string>(new MemoryBlockReference()))), null));

        Assert.Contains("Number", ex.Message);
    }

    [Fact]
    public void Bind_PropertyHasNoPublicSetter_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _binder.Bind(new NoSetterActivity(), Inputs(("ReadOnly", new InputArgument<string>(new MemoryBlockReference()))), null));

        Assert.Contains("public setter", ex.Message);
    }

    [Fact]
    public void Bind_WidensNarrowerInputArgumentOverTheSameMemoryBlock()
    {
        // #313: an InputArgument<object> property receiving an InputArgument<int> re-wraps over the same block.
        var activity = new WideningActivity();
        var reference = new MemoryBlockReference();

        _binder.Bind(activity, Inputs(("Value", new InputArgument<int>(reference))), null);

        Assert.NotNull(activity.Value);
        Assert.Same(reference, activity.Value.MemoryBlockReference());
    }

    private static Dictionary<string, InputArgument> Inputs(params (string Name, InputArgument Argument)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Argument);

    private sealed class SingleInputActivity : ActivityBase
    {
        public InputArgument<string> Text { get; set; } = null!;
        protected override void Execute(IActivityExecutionContext context) { }
    }

    private sealed class TypedInputActivity : ActivityBase
    {
        public InputArgument<int> Number { get; set; } = null!;
        protected override void Execute(IActivityExecutionContext context) { }
    }

    private sealed class NoSetterActivity : ActivityBase
    {
        public InputArgument<string> ReadOnly { get; } = null!;
        protected override void Execute(IActivityExecutionContext context) { }
    }

    private sealed class WideningActivity : ActivityBase
    {
        public InputArgument<object> Value { get; set; } = null!;
        protected override void Execute(IActivityExecutionContext context) { }
    }
}
