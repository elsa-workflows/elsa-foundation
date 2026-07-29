using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

internal static class SqliteStaticTelemetryFailureProbe
{
    private static ActivityListener? _listener;
    private static int _failureCount;

    public static int FailureCount => Volatile.Read(ref _failureCount);

    [ModuleInitializer]
    internal static void Initialize()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
            {
                if (source.Name != SqliteGroundworkTelemetry.ActivitySourceName)
                    return false;

                if (Interlocked.CompareExchange(ref _failureCount, 1, 0) == 0)
                    throw new InvalidOperationException("source discovery failure");

                return true;
            },
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }
}
