using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>The record sessions plus the bounded and command document sessions used by one explicitly bound OpenTelemetry adapter.</summary>
public sealed record GroundworkOpenTelemetryStores(
    IDiagnosticRecordStore Traces,
    IDiagnosticRecordStore Spans,
    IDiagnosticRecordStore MetricPoints,
    IDiagnosticRecordStore Logs,
    IDocumentStore Documents,
    IBoundedDocumentStore DocumentQueries);
