using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>
/// A deterministic leaf activity that always faults by throwing during execution. Used by execution-test
/// graphs to assert how composites react to a faulted child (e.g. the fault-aware <c>Parallel</c> join, #308).
/// </summary>
public sealed class FaultingActivity : Activity<ActivityUnit>
{
    /// <summary>The activity type discriminator used for faulting nodes.</summary>
    public const string FaultingActivityType = "test/faulting";

    [ActivityInput]
    public string Message { get; set; } = "Test activity faulted.";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault("TEST-FAULT", Message)));
}
