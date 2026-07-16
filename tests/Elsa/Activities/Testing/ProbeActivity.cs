using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Testing;

/// <summary>
/// A deterministic leaf activity used by execution-test graphs. It records that it ran (by virtue of
/// producing an <see cref="ActivityExecutionState"/>) and emits a fixed set of outcomes, letting tests
/// assert which nodes executed and which connections/branches were followed.
/// </summary>
public sealed class ProbeActivity : Activity<ActivityUnit>
{
    /// <summary>The activity type discriminator used for probe nodes.</summary>
    public const string ProbeActivityType = "test/probe";

    [ActivityInput]
    public IReadOnlyCollection<string> Outcomes { get; set; } = [ActivityOutcomes.Done];

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, Outcomes.Single()));
}
