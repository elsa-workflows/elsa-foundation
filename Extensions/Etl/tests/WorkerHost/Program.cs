using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
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
var acceptanceGate = new AcceptanceGate(root, role);

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

internal sealed class AcceptanceGate(string root, string role)
{
    private readonly TaskCompletionSource _markerPublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _publicationStarted;

    public async Task WaitBeforeAcceptanceAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _publicationStarted, 1, 0) == 0)
        {
            try
            {
                await EtlWorkerProtocol.WriteAcceptanceAsync(root, role,
                    new EtlWorkerAcceptance(lease, acceptance, Environment.ProcessId), cancellationToken);
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
        return await Inner.AcceptBatchAsync(lease, acceptance, cancellationToken);
    }
}
