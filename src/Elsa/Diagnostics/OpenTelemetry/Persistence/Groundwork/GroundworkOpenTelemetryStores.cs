using Groundwork.Documents.Scoping;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// The admitted command-only document surface used by OpenTelemetry persistence. All collection queries
/// go through the separately supplied <see cref="IBoundedDocumentStore"/>.
/// </summary>
public interface IGroundworkOpenTelemetryDocumentCommands
{
    DocumentStoreAccess Access { get; }

    Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default);

    Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The record sessions plus the bounded and command document sessions used by one explicitly bound OpenTelemetry adapter.</summary>
public sealed record GroundworkOpenTelemetryStores(
    IDiagnosticRecordStore Traces,
    IDiagnosticRecordStore Spans,
    IDiagnosticRecordStore MetricPoints,
    IDiagnosticRecordStore Logs,
    IGroundworkOpenTelemetryDocumentCommands Documents,
    IBoundedDocumentStore DocumentQueries)
{
    public GroundworkOpenTelemetryStores(
        IDiagnosticRecordStore traces,
        IDiagnosticRecordStore spans,
        IDiagnosticRecordStore metricPoints,
        IDiagnosticRecordStore logs,
        IDocumentStore documents,
        IBoundedDocumentStore documentQueries)
        : this(
            traces,
            spans,
            metricPoints,
            logs,
            new GroundworkOpenTelemetryDocumentCommands(documents),
            documentQueries)
    {
    }

    private sealed class GroundworkOpenTelemetryDocumentCommands(IDocumentStore documents)
        : IGroundworkOpenTelemetryDocumentCommands
    {
        private readonly IDocumentStore _documents =
            documents ?? throw new ArgumentNullException(nameof(documents));

        public DocumentStoreAccess Access => _documents.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            _documents.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            _documents.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            _documents.DeleteAsync(request, cancellationToken);
    }
}
