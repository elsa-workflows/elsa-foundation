using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// Test-only leaf body that reads an <see cref="int"/> <c>Value</c> input — bound by the test to a
/// <c>Variable</c> expression referencing the loop's per-iteration <c>index</c> variable — and emits the
/// resolved value as an <c>idx:{value}</c> outcome. Because the value flows through the real
/// materializer/expression-evaluator path (not a stub), the recorded outcome proves the index resolved
/// end-to-end in the body's scope chain.
/// </summary>
public sealed class IndexCaptureActivity : CodeActivity
{
    public const string ActivityType = "test/index-capture";
    public const string OutcomePrefix = "idx:";

    public IndexCaptureActivity()
        : base(ActivityType)
    {
    }

    public InputArgument<int> Value { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var value = context.Get(Value);
        context.SetOutcomes([OutcomePrefix + value.ToString(CultureInfo.InvariantCulture)]);
    }
}

/// <summary>Descriptor payload carried by an index-capture node.</summary>
public sealed record IndexCaptureDescriptor;

/// <summary>Constructs <see cref="IndexCaptureActivity"/> instances, wiring the resolved <c>Value</c> input.</summary>
public sealed class IndexCaptureActivityConstructor : IActivityConstructor<IndexCaptureDescriptor>
{
    public static string ConsumerKeyValue => typeof(IndexCaptureDescriptor).FullName!;
    public string ConsumerKey => ConsumerKeyValue;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) =>
        Construct(new IndexCaptureDescriptor(), inputs, outputs, cancellationToken);

    public ValueTask<IActivity> Construct(
        IndexCaptureDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var activity = new IndexCaptureActivity();
        if (inputs is not null && inputs.TryGetValue("Value", out var value))
            activity.Value = (InputArgument<int>)value;
        return new(activity);
    }
}
