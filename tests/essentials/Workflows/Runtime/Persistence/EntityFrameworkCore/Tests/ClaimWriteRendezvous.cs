using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Holds each participant's first recurring-occurrence claim write until every participant has reached its own, so all of
/// them read the due row before any of them claims it (#2198).
/// </summary>
internal sealed class ClaimWriteRendezvous(int participants)
{
    private readonly TaskCompletionSource _met = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public IInterceptor Participant() => new ClaimWriteHold(this);

    private Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) == participants)
            _met.TrySetResult();
        return _met.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private sealed class ClaimWriteHold(ClaimWriteRendezvous rendezvous) : SaveChangesInterceptor
    {
        private bool _held;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var claiming = eventData.Context!.ChangeTracker.Entries<RecurringTriggerScheduleEntity>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.ClaimOwnerId is not null);
            if (claiming && !_held)
            {
                _held = true;
                await rendezvous.ArriveAsync(cancellationToken);
            }

            return result;
        }
    }
}
