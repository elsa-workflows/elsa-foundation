using System.Diagnostics.Metrics;

namespace Kernel.Sim;

internal sealed class EventTicketMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();

    internal EventTicketMeasurements(string kind)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Elsa.Kernel.Durable" && instrument.Name == "elsa.kernel.events.ticket_failures")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, count, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "kind" && Equals(tag.Value, kind)) Failures += count;
        });
        _listener.Start();
    }

    internal long Failures { get; private set; }
    public void Dispose() => _listener.Dispose();
}
