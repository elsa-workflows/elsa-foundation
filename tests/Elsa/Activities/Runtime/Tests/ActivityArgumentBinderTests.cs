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

    [Fact]
    public void Bind_RewrapsWiderOutputArgumentOverTheSameMemoryBlock()
    {
        // #383: production output capture materializes OutputArgument<object?>; a typed result property
        // (e.g. OutputArgument<string> on ReadLine/Inline) must re-wrap it over the same block. Safe
        // because the activity writes a string into an object-typed capture slot (contravariant).
        var activity = new TypedResultActivity();
        var reference = new MemoryBlockReference();

        _binder.Bind(activity, null, Outputs(("Result", new OutputArgument<object?>(reference))));

        Assert.NotNull(activity.Result);
        Assert.Same(reference, activity.Result.MemoryBlockReference());
    }

    [Fact]
    public void Bind_OutputArgumentWithIncompatibleValueType_Throws()
    {
        // int and string are unrelated in both directions; no rewrap, clear failure.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _binder.Bind(new TypedResultActivity(), null, Outputs(("Result", new OutputArgument<int>(new MemoryBlockReference())))));

        Assert.Contains("Result", ex.Message);
    }

    private static Dictionary<string, InputArgument> Inputs(params (string Name, InputArgument Argument)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Argument);

    private static Dictionary<string, OutputArgument> Outputs(params (string Name, OutputArgument Argument)[] pairs) =>
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

    private sealed class TypedResultActivity : ActivityBase
    {
        public OutputArgument<string> Result { get; set; } = null!;
        protected override void Execute(IActivityExecutionContext context) { }
    }
}
