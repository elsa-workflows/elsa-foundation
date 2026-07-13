using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Elsa.Workflows.Runtime.Http.Diagnostics;

public static class HttpRouteTableTelemetry
{
    public const string ActivitySourceName = "Elsa.Workflows.Runtime.Http";
    public const string MeterName = ActivitySourceName;
    public const string ActivityName = "elsa.http.route_table.refresh";
    public const string DurationInstrumentName = "elsa.http.route_table.refresh.duration";
    public const string OutcomeTag = "elsa.activation.outcome";
    public const string RouteCountTag = "elsa.route.count";

    public const string SuccessOutcome = "success";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";

    private static readonly Lazy<ActivitySource> ActivitySource = new(
        () => new(ActivitySourceName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Meter> Meter = new(
        () => new(MeterName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Histogram<double>> Duration = new(
        () => Meter.Value.CreateHistogram<double>(
            DurationInstrumentName,
            "ms",
            "HTTP workflow route-table refresh duration."),
        LazyThreadSafetyMode.PublicationOnly);

    internal static ActivitySource GetActivitySource() => ActivitySource.Value;

    internal static Histogram<double> GetDuration() => Duration.Value;
}
