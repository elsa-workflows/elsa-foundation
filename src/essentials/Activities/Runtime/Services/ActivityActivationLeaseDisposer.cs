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

    /// <summary>Combines <paramref name="primary"/> and <paramref name="disposal"/> into one exception, keeping the classification of <paramref name="primary"/>.</summary>
    public static Exception Combine(Exception primary, Exception disposal) =>
        new ActivityActivationCleanupException(primary, disposal);
}
