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

        switch (Activity)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }

        if (ownedScope is not null)
            await ownedScope.DisposeAsync();
    }
}
