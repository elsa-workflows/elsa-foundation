using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Sqlite.DiagnosticRecords;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

internal sealed class OpenTelemetryGroundworkSqliteFixture : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"elsa-open-telemetry-{Guid.NewGuid():N}.db");

    public GroundworkOpenTelemetryBinding Binding { get; } =
        GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");

    public async Task<GroundworkOpenTelemetryStores> CreateProvidersAsync()
    {
        var connectionString = $"Data Source={_databasePath}";
        var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(Binding);
        var traces = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[0]);
        var spans = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[1]);
        var points = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[2]);
        var logs = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[3]);
        var documents = await SqliteDocumentStoreFactory.CreateAsync(
            connectionString,
            OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            DocumentStoreAccess.Scoped(Binding.DocumentStorageScope));
        return new(traces, spans, points, logs, documents);
    }

    public async ValueTask<GroundworkOpenTelemetryStore> CreateStoreAsync()
    {
        var providers = await CreateProvidersAsync();
        return CreateStore(providers);
    }

    public GroundworkOpenTelemetryStore CreateStore(
        GroundworkOpenTelemetryStores providers,
        TimeProvider? timeProvider = null) => new(
        providers,
        Options.Create(new OpenTelemetryDiagnosticsOptions()),
        Binding,
        timeProvider);

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return ValueTask.CompletedTask;
    }
}
