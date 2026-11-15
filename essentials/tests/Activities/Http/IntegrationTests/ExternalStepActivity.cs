using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A leaf activity that completes at once and declares no side-effect profile, so the runtime treats it as External
/// (ADR 0032) and never fuses it into the drain: each of its hops goes through the drain's own continuation delivery.
/// Discovered and registered like <see cref="StallingActivity"/>.
/// </summary>
public sealed class ExternalStepActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
