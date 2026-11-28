using System.Diagnostics.Metrics;

namespace Kernel.Durable;

internal static class KernelMetrics
{
    internal static readonly Meter Meter = new("Elsa.Kernel.Durable");
    internal static readonly Lazy<Counter<long>> EventTicketFailures = CreateCounter("elsa.kernel.events.ticket_failures", "{attempt}",
        "Number of committed event ticket step failures.");
    internal static readonly Lazy<Counter<long>> CascadePages = CreateCounter("elsa.kernel.cascade.pages", "{page}",
        "Number of termination cascade pages committed.");

    internal static Lazy<Counter<long>> CreateCounter(string name, string unit, string description) =>
        new(() => Meter.CreateCounter<long>(name, unit, description), LazyThreadSafetyMode.PublicationOnly);

    internal static void Add(Lazy<Counter<long>> counter, long value, KeyValuePair<string, object?> tag)
    {
        try { counter.Value.Add(value, tag); }
        catch (Exception)
        {
            // MeterListener callbacks run on the committing thread. A telemetry failure must leave the landed answer intact.
        }
    }
}
