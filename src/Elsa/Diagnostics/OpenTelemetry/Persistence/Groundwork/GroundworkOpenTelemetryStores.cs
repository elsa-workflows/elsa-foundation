using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>The five provider sessions used by one explicitly bound OpenTelemetry adapter.</summary>
public sealed record GroundworkOpenTelemetryStores(
    IDiagnosticRecordStore Traces,
    IDiagnosticRecordStore Spans,
    IDiagnosticRecordStore MetricPoints,
    IDiagnosticRecordStore Logs,
    IDocumentStore Documents);
