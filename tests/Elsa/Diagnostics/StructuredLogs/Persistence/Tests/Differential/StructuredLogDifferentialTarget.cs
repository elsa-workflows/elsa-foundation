using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Groundwork.Sqlite.DiagnosticRecords;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests.Differential;

/// <summary>
/// One comparand in the #646 EF-vs-Groundwork differential: a durable <see cref="IStructuredLogStore"/>
/// bound to a private on-disk SQLite database that survives <see cref="CloseAsync"/>, so restart and
/// ungraceful-stop probes observe real durability rather than a still-live process buffer.
/// </summary>
/// <remarks>
/// Both comparands run the same provider family (SQLite) on purpose. EF Core has no PostgreSQL or SQL
/// Server wiring anywhere in <c>src/</c>, so SQLite is the only provider on which the two stacks can be
/// compared at all. Groundwork's other three providers are covered by its own conformance matrix, which
/// is strictly less evidence than a differential and must not be presented as equivalent.
/// </remarks>
internal abstract class StructuredLogDifferentialTarget : IAsyncDisposable
{
    protected static readonly StructuredLogStoreBinding Binding = StructuredLogStoreBinding.Default;

    private readonly string _databasePath;

    protected StructuredLogDifferentialTarget(string identity)
    {
        Identity = identity;
        _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-differential-{identity}-{Guid.NewGuid():N}.db");
    }

    /// <summary>Stable comparand identity recorded in the divergence ledger.</summary>
    public string Identity { get; }

    protected string DatabasePath => _databasePath;

    /// <summary>Opens a started store over the durable storage, materializing schema on first use.</summary>
    public abstract Task<IStructuredLogStore> OpenAsync();

    /// <summary>Completes the capture drain and releases provider resources, leaving storage intact.</summary>
    public abstract Task CloseAsync();

    /// <summary>
    /// Releases provider resources <em>without</em> completing the capture drain, modelling process loss
    /// between an acknowledged append and a graceful shutdown.
    /// </summary>
    public abstract Task AbandonAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch
        {
            // Teardown must not mask a probe's own failure; storage deletion below is what matters here.
        }

        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static StructuredLogDifferentialTarget EfCore() => new EfCoreTarget();

    public static StructuredLogDifferentialTarget Groundwork() => new GroundworkTarget();

    private sealed class EfCoreTarget() : StructuredLogDifferentialTarget("efcore.sqlite")
    {
        private StructuredLogsTestHost? _host;
        private EfCoreStructuredLogStore? _store;

        public override Task<IStructuredLogStore> OpenAsync()
        {
            _host = StructuredLogsTestHost.Create(fileDataSource: DatabasePath);
            _store = new(_host, Options.Create(new StructuredLogsOptions()));
            _store.StartDraining();
            return Task.FromResult<IStructuredLogStore>(_store);
        }

        public override async Task CloseAsync()
        {
            if (_store is not null)
            {
                await _store.CompleteDrainingIfStartedAsync();
                await _store.DisposeAsync();
                _store = null;
            }

            _host?.Dispose();
            _host = null;
        }

        public override Task AbandonAsync()
        {
            // Drop the drain loop without completing it, then release the connection the file depends on.
            _store = null;
            _host?.Dispose();
            _host = null;
            return Task.CompletedTask;
        }
    }

    private sealed class GroundworkTarget() : StructuredLogDifferentialTarget("groundwork.sqlite")
    {
        private SqliteDiagnosticRecordStore? _provider;
        private GroundworkStructuredLogStore? _store;

        public override async Task<IStructuredLogStore> OpenAsync()
        {
            _provider = await SqliteDiagnosticRecordStoreFactory.CreateAsync(
                $"Data Source={DatabasePath}",
                GroundworkStructuredLogStore.CreateStreamDefinition(Binding.StreamId));
            _store = new(_provider, Options.Create(new StructuredLogsOptions()), Binding);
            _store.Start();
            return _store;
        }

        public override async Task CloseAsync()
        {
            if (_store is not null)
            {
                // DisposeAsync completes the capture drain under the configured shutdown timeout.
                await _store.DisposeAsync();
                _store = null;
            }

            _provider = null;
        }

        public override Task AbandonAsync()
        {
            // The provider store holds no disposable session of its own, so dropping both references is
            // the closest analogue to process loss that this stack exposes.
            _store = null;
            _provider = null;
            return Task.CompletedTask;
        }
    }
}
