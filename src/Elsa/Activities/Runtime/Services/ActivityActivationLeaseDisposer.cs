using Elsa.Activities.Runtime.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Closes an activation lease without letting cleanup failure escape the runtime fault boundary.</summary>
internal static class ActivityActivationLeaseDisposer
{
    public static async ValueTask<Exception?> TryDisposeAsync(ActivityActivationLease? lease)
    {
        if (lease is null)
            return null;

        try
        {
            await lease.DisposeAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public static Exception Combine(Exception primary, Exception disposal) =>
        new AggregateException("Activity execution and activation disposal both failed.", primary, disposal);
}
