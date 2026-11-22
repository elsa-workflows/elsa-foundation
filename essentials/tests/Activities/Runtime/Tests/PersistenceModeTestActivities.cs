using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// What the persistence-mode test activities report. Each test registers its own as a singleton, so a count or a
/// one-time action belongs to that test's host alone.
/// </summary>
public sealed class ActivityRunLog
{
    private int _executions;
    private int _ownershipTaken;

    /// <summary>How often <see cref="CountingReplaySafeActivity"/> ran.</summary>
    public int Executions => Volatile.Read(ref _executions);

    internal void RecordExecution() => Interlocked.Increment(ref _executions);

    internal bool TakeOwnershipOnce() => Interlocked.Exchange(ref _ownershipTaken, 1) == 0;
}

/// <summary>A ReplaySafe leaf that counts how often it runs, so a test can tell a run that executed once from one that was started again.</summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class CountingReplaySafeActivity(ActivityRunLog runs) : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        runs.RecordExecution();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>
/// A ReplaySafe leaf that takes its run's ownership the first time it runs, the way another node does when it
/// considers this node gone.
/// </summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class LeaseStealingReplaySafeActivity(IRuntimeExecutionOwnershipService ownership, ActivityRunLog runs) : Activity<ActivityUnit>
{
    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (runs.TakeOwnershipOnce())
            await ownership.AcquireAsync(context.WorkflowExecutionId);
        return ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done);
    }
}
