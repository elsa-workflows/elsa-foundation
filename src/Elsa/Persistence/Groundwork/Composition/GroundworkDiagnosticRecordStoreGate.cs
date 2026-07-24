using System.Runtime.ExceptionServices;
using Groundwork.DiagnosticRecords;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Defers access to one admitted diagnostic-record store until the host lifecycle has completed
/// its read-only deployment admission and provider-session acquisition.
/// </summary>
public sealed class GroundworkDiagnosticRecordStoreGate :
    IDiagnosticRecordStore,
    IDiagnosticAppendHandler,
    IDiagnosticQueryHandler,
    IDiagnosticInspectHandler,
    IDiagnosticTrimHandler
{
    private readonly Lock _gate = new();
    private IDiagnosticRecordStore? _store;
    private ExceptionDispatchInfo? _failure;
    private bool _released;

    public GroundworkDiagnosticRecordStoreGate()
    {
        Handlers = new(this, this, this, this);
    }

    public DiagnosticRecordStoreHandlers Handlers { get; }

    public DiagnosticQueryHandlerCapabilities Capabilities =>
        GetStore().Handlers.Query.Capabilities;

    public void Publish(IDiagnosticRecordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (_gate)
        {
            if (_released)
                throw new ObjectDisposedException(nameof(GroundworkDiagnosticRecordStoreGate));
            if (_failure is not null)
                throw new InvalidOperationException("A failed diagnostic-record store gate cannot publish a store.");
            if (_store is not null)
                throw new InvalidOperationException("The diagnostic-record store gate has already published a store.");
            _store = store;
        }
    }

    public void PublishFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_gate)
        {
            if (_store is not null || _failure is not null)
                return;
            _failure = ExceptionDispatchInfo.Capture(failure);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _store = null;
            _released = true;
        }
    }

    public ValueTask<DiagnosticAppendResult> AppendAsync(
        DiagnosticRecordBatch batch,
        CancellationToken cancellationToken = default) =>
        GetStore().Handlers.Append.AppendAsync(batch, cancellationToken);

    public ValueTask<DiagnosticRecordPage> QueryAsync(
        DiagnosticRecordQuery query,
        CancellationToken cancellationToken = default) =>
        GetStore().Handlers.Query.QueryAsync(query, cancellationToken);

    public ValueTask<DiagnosticStreamStatistics> InspectAsync(
        DiagnosticStreamInspectionRequest request,
        CancellationToken cancellationToken = default) =>
        GetStore().Handlers.Inspect.InspectAsync(request, cancellationToken);

    public ValueTask<DiagnosticTrimResult> TrimAsync(
        DiagnosticTrimRequest request,
        CancellationToken cancellationToken = default) =>
        GetStore().Handlers.Trim.TrimAsync(request, cancellationToken);

    private IDiagnosticRecordStore GetStore()
    {
        ExceptionDispatchInfo? failure;
        lock (_gate)
        {
            if (_store is not null)
                return _store;
            failure = _failure;
            if (failure is null && _released)
                throw new ObjectDisposedException(nameof(GroundworkDiagnosticRecordStoreGate));
        }

        failure?.Throw();
        throw new InvalidOperationException(
            "The diagnostic-record store has not completed startup admission.");
    }
}
