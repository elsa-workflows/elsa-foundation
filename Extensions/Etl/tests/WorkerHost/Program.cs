using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Output;
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
ArgumentNullException.ThrowIfNull(configuration.Scope);
ArgumentNullException.ThrowIfNull(configuration.Bindings);
if (configuration.Bindings.Any(binding => binding.TenantId != configuration.Scope.TenantId ||
        !StringComparer.Ordinal.Equals(binding.PersistencePartition, configuration.Scope.PersistencePartition)))
    throw new InvalidDataException("Every ETL worker file binding must use the configured scope.");
var root = Path.GetFullPath(configuration.Root);
Directory.CreateDirectory(root);
Directory.CreateDirectory(Path.Combine(root, "snapshots"));
Directory.CreateDirectory(EtlWorkerProtocol.JournalDirectory(root));

var builder = Host.CreateApplicationBuilder([]);
var runtime = new EtlRuntimeFeature
{
    SnapshotRoot = Path.Combine(root, "snapshots"),
    FileBindings = configuration.Bindings
};
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

var storeDescriptor = builder.Services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEtlRuntimeStore))
    ?? throw new InvalidOperationException("The ETL EF persistence feature did not register IEtlRuntimeStore.");
var storeImplementationType = storeDescriptor.ImplementationType
    ?? throw new InvalidOperationException("The ETL runtime store registration must use an implementation type.");
builder.Services.Remove(storeDescriptor);
builder.Services.Add(new ServiceDescriptor(typeof(IEtlRuntimeStore), services =>
{
    var inner = (IEtlRuntimeStore)ActivatorUtilities.CreateInstance(services, storeImplementationType);
    return new AcceptanceGateStore(inner, acceptanceGate);
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
