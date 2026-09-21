using System.Diagnostics;
using System.Runtime.CompilerServices;
using Elsa.Workbench.Readiness;

namespace Elsa.Modularity.Tests;

internal static class ShellStaticTelemetryFailureProbe
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
                if (source.Name != ShellActivationTelemetry.ActivitySourceName)
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
