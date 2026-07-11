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

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        DurationInstrumentName,
        "ms",
        "HTTP workflow route-table refresh duration.");
}
