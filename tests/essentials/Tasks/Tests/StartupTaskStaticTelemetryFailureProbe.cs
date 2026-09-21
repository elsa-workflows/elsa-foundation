using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Elsa.Tasks.Diagnostics;

namespace Elsa.Tasks.Tests;

internal static class StartupTaskStaticTelemetryFailureProbe
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
                if (instrument.Meter.Name != StartupTaskTelemetry.MeterName || instrument.Name != StartupTaskTelemetry.DurationInstrumentName)
                    return;

                if (Interlocked.CompareExchange(ref _failureCount, 1, 0) == 0)
                    throw new InvalidOperationException("instrument publication failure");

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.Start();
    }
}
