namespace Elsa.Diagnostics.OpenTelemetry.Ingestion;

/// <summary>The three OTLP/HTTP protobuf signal payloads supported by the receiver.</summary>
public enum OtlpSignal
{
    Traces = 0,
    Metrics = 1,
    Logs = 2
}
