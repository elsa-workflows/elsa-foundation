using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// Test-only leaf body that reads an <see cref="int"/> <c>Value</c> input — bound by the test to a
/// <c>Variable</c> expression referencing the loop's per-iteration <c>index</c> variable — and emits the
/// resolved value as an <c>idx:{value}</c> outcome. Because the value flows through the real
/// materializer/expression-evaluator path (not a stub), the recorded outcome proves the index resolved
/// end-to-end in the body's scope chain.
/// </summary>
public sealed class IndexCaptureActivity : Activity<IndexCaptureResult>
{
    public const string ActivityType = "test/index-capture";
    [ActivityInput]
    public int Value { get; set; }

    protected override ValueTask<ActivityTransition<IndexCaptureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new IndexCaptureResult(Value)));
}

public sealed record IndexCaptureResult([property: Output] int Value);
