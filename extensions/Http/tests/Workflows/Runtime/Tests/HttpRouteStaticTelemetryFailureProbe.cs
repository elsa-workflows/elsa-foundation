using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Elsa.Workflows.Runtime.Http.Tests;

internal static class HttpRouteStaticTelemetryFailureProbe
{
    private static MeterListener? _listener;
    private static int _failureCount;

    public static int FailureCount => Volatile.Read(ref _failureCount);

    [ModuleInitializer]
    internal static void Initialize()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != "Elsa.Workflows.Runtime.Http" || instrument.Name != "elsa.http.route_table.refresh.duration")
                    return;

                if (Interlocked.CompareExchange(ref _failureCount, 1, 0) == 0)
                    throw new InvalidOperationException("instrument publication failure");

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.Start();
    }
}
