namespace Elsa.Diagnostics.OpenTelemetry.Permissions;

/// <summary>
/// The catalog action used to guard the OpenTelemetry diagnostics query and live-stream
/// endpoints. It is default-permissive (anonymous) until the host enables endpoint security, after which
/// the host assigns this permission to authorized principals. The OTLP ingestion (collector) endpoints are
/// not guarded by this permission; they are authenticated separately via <c>OtlpIngestionSecurity</c>
/// (API-key header or loopback allowance).
/// </summary>
public static class OpenTelemetryPermissions
{
    public const string OwnerId = "Elsa.Diagnostics.OpenTelemetry";
    public const string Read = "Diagnostics:OpenTelemetry.Read";
    public const string LegacyPolicy = "Diagnostics:OpenTelemetry";
    // Source compatibility for callers that used the old constant; endpoint metadata must use Read.
    public const string Policy = Read;
}
