using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Tests.WorkerHost;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

if (args.Length != 2)
    throw new ArgumentException("Usage: Elsa.Etl.WorkerHost <configuration-path> <role>");

var configurationPath = Path.GetFullPath(args[0]);
var role = args[1];
EtlWorkerProtocol.ValidateRole(role);
var configuration = await EtlWorkerProtocol.ReadConfigurationAsync(configurationPath);
if (!Enum.IsDefined(configuration.WritePause))
    throw new InvalidDataException("Unsupported fixture write-pause stage.");
if (!Enum.IsDefined(configuration.StoreGate))
    throw new InvalidDataException("Unsupported fixture runtime-store gate.");
if (!Enum.IsDefined(configuration.ConnectorGate))
    throw new InvalidDataException("Unsupported fixture connector-store gate.");
ArgumentNullException.ThrowIfNull(configuration.Scope);
ArgumentNullException.ThrowIfNull(configuration.Bindings);
var httpBindings = configuration.HttpBindings ?? [];
if (configuration.Bindings.Any(binding => binding.TenantId != configuration.Scope.TenantId ||
        !StringComparer.Ordinal.Equals(binding.PersistencePartition, configuration.Scope.PersistencePartition)))
    throw new InvalidDataException("Every ETL worker file binding must use the configured scope.");
if (httpBindings.Any(binding => binding.TenantId != configuration.Scope.TenantId ||
        !StringComparer.Ordinal.Equals(binding.PersistencePartition, configuration.Scope.PersistencePartition)))
    throw new InvalidDataException("Every ETL worker HTTP binding must use the configured scope.");
var root = Path.GetFullPath(configuration.Root);
Directory.CreateDirectory(root);
Directory.CreateDirectory(Path.Combine(root, "snapshots"));
Directory.CreateDirectory(EtlWorkerProtocol.JournalDirectory(root));

var builder = Host.CreateApplicationBuilder([]);
var runtime = new EtlRuntimeFeature
{
    SnapshotRoot = Path.Combine(root, "snapshots"),
    FileBindings = configuration.Bindings,
    HttpBindings = httpBindings,
    // Keep each child process to one active attempt so journal gates can coordinate which
    // process owns each partition; the process still runs the real runtime worker loop.
    MaxConcurrentAttempts = 1
};
// A held delivery must not be reclaimed by another attempt in the same fixture process.
// The recovery case deliberately delegates that next claim to the replacement process.
runtime.ConfigureServices(builder.Services);
if (configuration.WritePause != EtlWorkerWritePause.None)
    builder.Services.Replace(ServiceDescriptor.Singleton<IFileChunkPublisherFactory>(
        new StagingPausePublisherFactory(new FileChunkPublisherFactory(), configuration.Root, role, configuration.WritePause)));

var persistence = new EtlRuntimeEntityFrameworkCoreFeature
{
    ConnectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(root, "runtime.db"),
        Pooling = false
    }.ToString(),
    PreparationLeaseSeconds = 5,
    PartitionLeaseSeconds = 5,
    DeliveryLeaseSeconds = 5
};
persistence.ConfigureServices(builder.Services);
builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.Validate);
var acceptanceGate = new AcceptanceGate(root, role, configuration.StoreGate);
var connectorGate = new ConnectorWorkerGate(root, role, configuration.ConnectorGate);
if (configuration.StoreGate == EtlWorkerStoreGate.AfterDeliveryClaim)
    builder.Services.AddSingleton<IEtlResultDelivery>(new JournalOnlyResultDelivery(acceptanceGate));

var storeDescriptor = builder.Services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEtlRuntimeStore))
    ?? throw new InvalidOperationException("The ETL EF persistence feature did not register IEtlRuntimeStore.");
var storeImplementationType = storeDescriptor.ImplementationType
    ?? throw new InvalidOperationException("The ETL runtime store registration must use an implementation type.");
builder.Services.Remove(storeDescriptor);
builder.Services.Add(new ServiceDescriptor(typeof(IEtlRuntimeStore), services =>
{
    var inner = (IEtlRuntimeStore)ActivatorUtilities.CreateInstance(services, storeImplementationType);
    var acceptanceStore = new AcceptanceGateStore(inner, acceptanceGate);
    var connectorStore = inner as IEtlConnectorRuntimeStore
        ?? throw new InvalidOperationException("The ETL fixture SQLite store must provide connector persistence.");
    return new ConnectorWorkerGateStore(acceptanceStore, connectorStore, connectorGate);
}, storeDescriptor.Lifetime));

using var host = builder.Build();
await host.StartAsync();
Console.WriteLine($"{EtlWorkerProtocol.ReadyPrefix}{Environment.ProcessId}");
await host.WaitForShutdownAsync();
return 0;

internal sealed class AcceptanceGate(string root, string role, EtlWorkerStoreGate storeGate)
{
    private readonly TaskCompletionSource _markerPublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _publicationStarted;
    private int _committedAcceptanceOrdinal;

    public Task WaitBeforeAcceptanceAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance,
        CancellationToken cancellationToken) =>
        WaitAsync(EtlWorkerStoreGate.BeforeAcceptance,
            token => EtlWorkerProtocol.WriteAcceptanceAsync(root, role,
                new EtlWorkerAcceptance(lease, acceptance, Environment.ProcessId), token), cancellationToken);

    public Task WaitBeforeReservationAsync(EtlPartitionLease lease, EtlBatchDescriptor batch,
        CancellationToken cancellationToken) =>
        WaitAsync(EtlWorkerStoreGate.BeforeReservation,
            token => EtlWorkerProtocol.WriteReservationAsync(root, role,
                new EtlWorkerReservation(lease, batch, Environment.ProcessId), token), cancellationToken);

    public async Task WaitAfterAcceptanceCommitAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance,
        EtlAcceptanceResult result, CancellationToken cancellationToken)
    {
        if (storeGate != EtlWorkerStoreGate.AfterAcceptanceCommit)
            return;
        if (result.Status != EtlStoreStatus.Applied)
            throw new InvalidOperationException($"The after-acceptance fixture gate requires an Applied result, received {result.Status}.");

        await WaitAsync(EtlWorkerStoreGate.AfterAcceptanceCommit,
            token => EtlWorkerProtocol.WriteCommittedAcceptanceAsync(root, role,
                new EtlWorkerCommittedAcceptance(lease, acceptance, result, Environment.ProcessId), token), cancellationToken);
    }

    public Task RecordCommittedAcceptanceAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance,
        EtlAcceptanceResult result, CancellationToken cancellationToken)
    {
        if (result.Status != EtlStoreStatus.Applied)
            return Task.CompletedTask;

        var ordinal = Interlocked.Increment(ref _committedAcceptanceOrdinal);
        return EtlWorkerProtocol.WriteCommittedAcceptanceReceiptAsync(root, role, ordinal,
            new EtlWorkerCommittedAcceptance(lease, acceptance, result, Environment.ProcessId), cancellationToken);
    }

    public Task WaitAfterDeliveryClaimAsync(EtlDeliveryLease lease, CancellationToken cancellationToken) =>
        WaitAsync(EtlWorkerStoreGate.AfterDeliveryClaim,
            token => EtlWorkerProtocol.WriteDeliveryAsync(root, role,
                new EtlWorkerDelivery(lease, Environment.ProcessId), token), cancellationToken);

    private async Task WaitAsync(EtlWorkerStoreGate selectedStage, Func<CancellationToken, Task> publish,
        CancellationToken cancellationToken)
    {
        if (storeGate != selectedStage)
            return;

        if (Interlocked.CompareExchange(ref _publicationStarted, 1, 0) == 0)
        {
            try
            {
                await publish(cancellationToken);
                _markerPublication.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                _markerPublication.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                _markerPublication.TrySetException(exception);
            }
        }

        await _markerPublication.Task.WaitAsync(cancellationToken);
        await WaitForReleaseAsync(cancellationToken);
    }

    private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
    {
        var releasePath = EtlWorkerProtocol.ReleasePath(root, role);
        while (!File.Exists(releasePath))
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
    }
}

internal sealed class AcceptanceGateStore(IEtlRuntimeStore inner, AcceptanceGate gate)
    : ForwardingEtlRuntimeStore(inner)
{
    public override async ValueTask<EtlAcceptanceResult> AcceptBatchAsync(EtlPartitionLease lease,
        EtlBatchAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        await gate.WaitBeforeAcceptanceAsync(lease, acceptance, cancellationToken);
        var result = await Inner.AcceptBatchAsync(lease, acceptance, cancellationToken);
        await gate.RecordCommittedAcceptanceAsync(lease, acceptance, result, cancellationToken);
        await gate.WaitAfterAcceptanceCommitAsync(lease, acceptance, result, cancellationToken);
        return result;
    }

    public override async ValueTask<EtlReservationResult> ReserveBatchAsync(EtlPartitionLease lease,
        EtlBatchDescriptor batch, CancellationToken cancellationToken = default)
    {
        await gate.WaitBeforeReservationAsync(lease, batch, cancellationToken);
        return await Inner.ReserveBatchAsync(lease, batch, cancellationToken);
    }
}

// This adapter observes a real claim but never dispatches native work or supplies an acknowledgement proof.
internal sealed class JournalOnlyResultDelivery(AcceptanceGate gate) : IEtlResultDelivery
{
    public async ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAfterDeliveryClaimAsync(delivery, cancellationToken);
        return null;
    }
}
