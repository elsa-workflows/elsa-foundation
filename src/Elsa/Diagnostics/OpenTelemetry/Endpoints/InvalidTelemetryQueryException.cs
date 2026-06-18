namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Raised by <see cref="OpenTelemetryTraceFilterBinder"/> when a live-stream query value is malformed.
/// The stream endpoint surfaces it as a <c>400 Bad Request</c> so raw parse failures never leak.
/// </summary>
public sealed class InvalidTelemetryQueryException(string message) : Exception(message);
