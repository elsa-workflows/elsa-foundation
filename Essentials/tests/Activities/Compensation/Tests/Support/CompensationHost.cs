using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Primitives;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// Host generations over one SQLite file in one persistence mode, with a clock the test can move past the leases a
/// stopped host leaves behind. The <see cref="Participant"/> is shared by every generation, as an external system is.
/// </summary>
public sealed class CompensationHost(PersistenceMode mode) : IDisposable
{
    public const string WorkflowExecutionId = "wfexec-compensation-nesting";
    public static readonly WorkflowExecutionPartition DefaultPartition = new(PersistenceScope.DefaultValue);

    private const string RecoverySigningKey = "compensation-nesting-recovery-signing-key-32";
    private const string HierarchySigningKey = "compensation-nesting-hierarchy-signing-key-32";

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    // Alteration plans outlive a host generation in the SQLite store; their protected payloads need the same key ring,
    // as a configured key ring gives every process.
    private readonly WorkflowAlterationEphemeralKeyRing _alterationKeys = new();
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-compensation-nesting-{Guid.NewGuid():N}.db");

    public PersistenceMode Mode => mode;

    /// <summary>The blocking incidents of the run, as the last <see cref="RunAsync"/> or <see cref="RecoverAsync"/> left them.</summary>
    public IReadOnlyCollection<IncidentState> BlockingIncidents { get; private set; } = [];

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    /// <summary>Publishes and runs <paramref name="root"/> on one host until it is quiet.</summary>
    public async Task<WorkflowExecutionRun> RunAsync(Participant participant, ExecutableNode root, Action<IServiceCollection>? configure = null)
    {
        await using var host = await StartHostAsync(participant, configure);
        await StartAsync(host, root);
        await host.SweepUntilQuietAsync();
        return await ReadAsync(host);
    }

    /// <summary>Publishes and starts <paramref name="root"/>; a host stopped during the pass ends the call quietly.</summary>
    public static async Task StartAsync(WorkflowExecutionHarness host, ExecutableNode root)
    {
        var reference = await host.PublishAsync(CompensationGraph.Executable(root), "source-compensation-nesting");
        try
        {
            await host.StartPublishedAsync(reference, WorkflowExecutionId, partition: DefaultPartition);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            // The host stopped during the pass, as a process shutdown stops it.
        }
    }

    // The stopped host's work-item and ownership leases outlive it. A restarted process waits them out; here the shared
    // clock moves past them before the new host sweeps.
    public async Task<WorkflowExecutionRun> RecoverAsync(WorkflowExecutionHarness host)
    {
        ExpireLeases();
        await host.SweepUntilQuietAsync();
        return await ReadAsync(host);
    }

    /// <summary>Moves the shared clock past every lease a stopped host left behind.</summary>
    public void ExpireLeases() => _clock.Advance(TimeSpan.FromHours(1));

    private async Task<WorkflowExecutionRun> ReadAsync(WorkflowExecutionHarness host)
    {
        BlockingIncidents = await host.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(WorkflowExecutionId);
        return await host.ReadRunAsync(WorkflowExecutionId);
    }

    public async Task<WorkflowExecutionHarness> StartHostAsync(Participant participant, Action<IServiceCollection>? configure = null)
    {
        var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesCompensationFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services
                    .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                    {
                        Provider = "Sqlite",
                        ConnectionString = $"Data Source={_databasePath};Pooling=False",
                        RecoveryContinuationSigningKey = RecoverySigningKey,
                        HierarchyCursorSigningKey = HierarchySigningKey
                    })
                    .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                services.AddPersistenceModes(options =>
                {
                    options.Mode = mode;
                    options.RecordCompletion = true;
                });
                services.AddSingleton(participant);
                HostKillSwitch.Install(services, new HostKillSwitch());
                services.Replace(ServiceDescriptor.Singleton<IRuntimeExecutionIdGenerator>(_ => new ShortRuntimeExecutionIdGenerator(TimeProvider.System)));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.Replace(ServiceDescriptor.Singleton(_alterationKeys));
                configure?.Invoke(services);
            })
            .Build();

        foreach (var initializer in host.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        host.InitializeActivityTypes();
        return host;
    }
}
