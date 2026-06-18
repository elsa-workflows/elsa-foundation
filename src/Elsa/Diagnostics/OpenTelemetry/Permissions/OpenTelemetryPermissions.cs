namespace Elsa.Diagnostics.OpenTelemetry.Permissions;

/// <summary>
/// The FastEndpoints permission used to guard the OpenTelemetry diagnostics query and live-stream
/// endpoints. It is default-permissive (anonymous) until the host enables endpoint security, after which
/// the host assigns this permission to authorized principals. The OTLP ingestion (collector) endpoints are
/// not guarded by this permission; they are authenticated separately via <c>OtlpIngestionSecurity</c>
/// (API-key header or loopback allowance).
/// </summary>
internal static class OpenTelemetryPermissions
{
    public const string Policy = "Diagnostics:OpenTelemetry";
}
