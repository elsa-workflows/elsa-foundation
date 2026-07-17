using System.Runtime.ExceptionServices;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Contracts;

public interface IActivityActivator
{
    ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityActivationRequest(
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null);

/// <summary>Owns one fresh activity object and the dependency scope used by its attempt.</summary>
public sealed class ActivityActivationLease(
    IActivity activity,
    IAsyncDisposable? ownedScope = null) : IAsyncDisposable
{
    private int _disposed;

    public IActivity Activity { get; } = activity ?? throw new ArgumentNullException(nameof(activity));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Exception? activityException = null;
        Exception? scopeException = null;
        try
        {
            switch (Activity)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception exception)
        {
            activityException = exception;
        }

        try
        {
            if (ownedScope is not null)
                await ownedScope.DisposeAsync();
        }
        catch (Exception exception)
        {
            scopeException = exception;
        }

        if (activityException is not null && scopeException is not null)
            throw new AggregateException("Both activity and activation-scope disposal failed.", activityException, scopeException);
        if (activityException is not null)
            ExceptionDispatchInfo.Capture(activityException).Throw();
        if (scopeException is not null)
            ExceptionDispatchInfo.Capture(scopeException).Throw();
    }
}
