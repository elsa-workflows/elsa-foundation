using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A deterministic leaf activity that stalls the inline drain for a fixed <see cref="Duration"/>, observing the
/// activity's <see cref="ActivityExecutionContext.CancellationToken"/> — the very token the sync-endpoint
/// middleware's per-endpoint <c>RequestTimeout</c> linked CTS cancels. It exists so a synchronous endpoint
/// (spec 089 sub-unit E, scenario 5.3) can be driven past its timeout: the stall throws
/// <see cref="OperationCanceledException"/> on the cancelled token, which propagates out of <c>RouteAsync</c> and
/// the middleware maps to <c>408</c>.
/// </summary>
/// <remarks>
/// Unlike <c>Delay</c> (which suspends durably on a bookmark and would drive the 202 <em>degrade</em> path, not a
/// timeout), this activity keeps the inline drain genuinely busy, so the timeout token is the only thing that can
/// end the wait. It is discovered and registered by <c>RegisterActivityTypesStartupTask</c> (it is an
/// <see cref="IActivity"/> in a loaded assembly) and transiently activated exactly like the other nodes the
/// fixture composes.
/// </remarks>
public sealed class StallingActivity : Activity<ActivityUnit>
{
    /// <summary>The duration to stall for, observing the execution cancellation token. Defaults to a long stall.</summary>
    [Elsa.Activities.Runtime.Core.Attributes.ActivityInput(Key = "duration")]
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        // The wait is bounded only by the token: a per-endpoint RequestTimeout cancels it and the stall throws
        // OperationCanceledException, which the middleware maps to 408. A well-behaved (untimed) run never reaches
        // here in the sync scenarios.
        await Task.Delay(Duration, context.CancellationToken);
        return ActivityTransition.Complete(ActivityUnit.Value);
    }
}
