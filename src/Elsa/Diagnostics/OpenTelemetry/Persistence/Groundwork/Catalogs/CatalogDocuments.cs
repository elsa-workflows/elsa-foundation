namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;

/// <summary>Stable document identities and JSON paths for the OpenTelemetry catalogs.</summary>
public static class CatalogDocuments
{
    public const string ResourceKind = "openTelemetryResource";
    public const string InstrumentKind = "openTelemetryMetricInstrument";
    public const string SchemaVersion = "1";

    public const string InstrumentResourceIdPath = "/resourceId";
    public const string ResourceIdSearchKeyPath = "/idSearchKey";
    public const string ServiceNamePath = "/serviceName";
    public const string ServiceNameComparisonKeyPath = "/serviceNameComparisonKey";
    public const string ServiceNameHashPath = "/serviceNameHash";
    public const string ServiceNameSearchKeyPath = "/serviceNameSearchKey";
    public const string ResourceStatusPath = "/status";
    public const string InstrumentNamePath = "/name";
    public const string InstrumentKindPath = "/kind";
    public const string LastSeenPath = "/lastSeenUtcTicks";
    public const string RetentionTieBreakerPath = "/retentionTieBreaker";
}
