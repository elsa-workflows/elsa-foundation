using System.Diagnostics.Metrics;

namespace Kernel.Sim;

internal sealed class CascadePageMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    internal long AtInstance;
    internal long AtMarker;
    internal long Other;

    internal CascadePageMeasurements(bool throwAfterMeasurement = false)
    {
        _listener.InstrumentPublished = (instrument, sink) =>
        {
            if (instrument is Counter<long> && instrument.Meter.Name == "Elsa.Kernel.Durable" &&
                instrument.Name == "elsa.kernel.cascade.pages") sink.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (tags is [{ Key: "where", Value: "instance" }]) Interlocked.Add(ref AtInstance, value);
            else if (tags is [{ Key: "where", Value: "retirement-marker" }]) Interlocked.Add(ref AtMarker, value);
            else Interlocked.Add(ref Other, value);
            if (throwAfterMeasurement) throw new InvalidOperationException("Test measurement listener failed.");
        });
        _listener.Start();
    }

    internal static void CheckFirstUse(Action<bool, string> check)
    {
        // A fresh instrument also exercises first use when a wider suite already enabled the cascade counter.
        const string name = "elsa.kernel.sim.cascade.publication";
        var firstUse = KernelMetrics.CreateCounter(name, "{page}", "Test publication.");
        var observed = false;
        using (var failing = new MeterListener())
        {
            failing.InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter == KernelMetrics.Meter && instrument.Name == name)
                {
                    observed = true;
                    throw new InvalidOperationException("Test publication listener failed.");
                }
            };
            failing.Start();
            KernelMetrics.Add(firstUse, 1, new("where", "instance"));
        }
        KernelMetrics.Add(firstUse, 1, new("where", "instance"));
        check(observed && firstUse.IsValueCreated,
            "PC-1 a failing first publication returns normally and the counter can be initialized after the listener leaves");
    }

    public void Dispose() => _listener.Dispose();
}
