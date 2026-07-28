using System.Runtime.ExceptionServices;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Scoping;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Owns the all-or-nothing provider acquisition for one OpenTelemetry adapter. Construction only builds
/// deferred gates; the lifecycle invokes <see cref="AcquireAsync"/> before its capture drain starts.
/// </summary>
internal sealed class GroundworkOpenTelemetryStartupResource
    : IDiagnosticsPersistenceStartupResource
{
    private readonly IDiagnosticRecordStoreSessionFactory _diagnosticSessionFactory;
    private readonly IDiagnosticRecordDeploymentManifestSource _deploymentSource;
    private readonly IGroundworkStoreSessionSource _documentSessionSource;
    private readonly GroundworkOpenTelemetryBinding _binding;
    private readonly GroundworkDiagnosticRecordStoreGate _traces = new();
    private readonly GroundworkDiagnosticRecordStoreGate _spans = new();
    private readonly GroundworkDiagnosticRecordStoreGate _metricPoints = new();
    private readonly GroundworkDiagnosticRecordStoreGate _logs = new();
    private readonly GroundworkOpenTelemetryDocumentStoreGate _documents;
    private readonly Lock _gate = new();
    private ExceptionDispatchInfo? _failure;
    private OpenTelemetryResourceLease? _lease;
    private bool _attempted;

    public GroundworkOpenTelemetryStartupResource(
        IDiagnosticRecordStoreSessionFactory diagnosticSessionFactory,
        IDiagnosticRecordDeploymentManifestSource deploymentSource,
        IGroundworkStoreSessionSource documentSessionSource,
        GroundworkOpenTelemetryBinding binding)
    {
        _diagnosticSessionFactory = diagnosticSessionFactory ?? throw new ArgumentNullException(nameof(diagnosticSessionFactory));
        _deploymentSource = deploymentSource ?? throw new ArgumentNullException(nameof(deploymentSource));
        _documentSessionSource = documentSessionSource ?? throw new ArgumentNullException(nameof(documentSessionSource));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        binding.ValidateAll();
        _documents = new(DocumentStoreAccess.Scoped(binding.DocumentStorageScope));
        Stores = new(_traces, _spans, _metricPoints, _logs, _documents, _documents);
    }

    public GroundworkOpenTelemetryStores Stores { get; }

    public async ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_lease is not null)
                return _lease;
            _failure?.Throw();
            if (_attempted)
                throw new InvalidOperationException("OpenTelemetry persistence startup did not produce a resource lease.");
            _attempted = true;
        }

        IDiagnosticRecordStoreSession? diagnosticSession = null;
        GroundworkStoreSessionResources? documentResources = null;
        try
        {
            var deployment = _deploymentSource.CreateDeploymentManifest();
            diagnosticSession = await _diagnosticSessionFactory.OpenAsync(
                deployment,
                _binding.DiagnosticScope,
                cancellationToken);

            var traces = await diagnosticSession.OpenStoreAsync(new(_binding.TraceStreamId), cancellationToken);
            var spans = await diagnosticSession.OpenStoreAsync(new(_binding.SpanStreamId), cancellationToken);
            var metricPoints = await diagnosticSession.OpenStoreAsync(new(_binding.MetricPointStreamId), cancellationToken);
            var logs = await diagnosticSession.OpenStoreAsync(new(_binding.LogStreamId), cancellationToken);
            documentResources = await _documentSessionSource.OpenAsync(
                DocumentStoreAccess.Scoped(_binding.DocumentStorageScope),
                cancellationToken);

            // Do not expose a partially opened adapter. A store becomes usable only after all five
            // backing resources exist and can be owned by the returned lifecycle lease.
            _traces.Publish(traces);
            _spans.Publish(spans);
            _metricPoints.Publish(metricPoints);
            _logs.Publish(logs);
            _documents.Publish(documentResources.DocumentStore, documentResources.BoundedDocumentStore);

            var lease = new OpenTelemetryResourceLease(
                _traces,
                _spans,
                _metricPoints,
                _logs,
                _documents,
                diagnosticSession,
                documentResources);
            lock (_gate)
                _lease = lease;
            return lease;
        }
        catch (Exception startupFailure)
        {
            _traces.PublishFailure(startupFailure);
            _spans.PublishFailure(startupFailure);
            _metricPoints.PublishFailure(startupFailure);
            _logs.PublishFailure(startupFailure);
            _documents.PublishFailure(startupFailure);
            lock (_gate)
                _failure = ExceptionDispatchInfo.Capture(startupFailure);

            await DisposePartialAsync(documentResources, diagnosticSession, startupFailure);
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
            throw;
        }
    }

    private static async ValueTask DisposePartialAsync(
        GroundworkStoreSessionResources? documentResources,
        IDiagnosticRecordStoreSession? diagnosticSession,
        Exception startupFailure)
    {
        if (documentResources?.Lease is not null)
        {
            try
            {
                await documentResources.Lease.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                startupFailure.Data["OpenTelemetryDocumentStartupCleanupFailure"] = cleanupFailure;
            }
        }

        if (diagnosticSession is null)
            return;

        try
        {
            await diagnosticSession.DisposeAsync();
        }
        catch (Exception cleanupFailure)
        {
            startupFailure.Data["OpenTelemetryDiagnosticStartupCleanupFailure"] = cleanupFailure;
        }
    }

    private sealed class OpenTelemetryResourceLease(
        GroundworkDiagnosticRecordStoreGate traces,
        GroundworkDiagnosticRecordStoreGate spans,
        GroundworkDiagnosticRecordStoreGate metricPoints,
        GroundworkDiagnosticRecordStoreGate logs,
        GroundworkOpenTelemetryDocumentStoreGate documents,
        IDiagnosticRecordStoreSession diagnosticSession,
        GroundworkStoreSessionResources documentResources)
        : IDiagnosticsPersistenceResourceLease
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            traces.Release();
            spans.Release();
            metricPoints.Release();
            logs.Release();
            documents.Release();

            Exception? failure = null;
            if (documentResources.Lease is not null)
            {
                try
                {
                    await documentResources.Lease.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            try
            {
                await diagnosticSession.DisposeAsync();
            }
            catch (Exception exception)
            {
                if (failure is null)
                    failure = exception;
                else
                    failure.Data["OpenTelemetryDiagnosticShutdownCleanupFailure"] = exception;
            }

            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

internal sealed class OpenTelemetryDirectStoreResourceLease : IDiagnosticsPersistenceResourceLease
{
    public static OpenTelemetryDirectStoreResourceLease Instance { get; } = new();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
