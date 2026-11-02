using System.Collections.Concurrent;
using Elsa.BoundaryProbe;
using System.Data.Common;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Http.IntegrationTests;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Dispatch = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[0]))!;
if (config.Action is not ("run" or "recover" or "reclaim" or "initialize")) throw new ArgumentException("Unsupported qualification action.");
if (File.Exists(config.Result)) throw new IOException("Preserve the existing qualification result and use a fresh output.");
if (config.Provider is not ("Sqlite" or "PostgreSql")) throw new ArgumentException("Unsupported qualification provider.");
var connection = config.Provider == "Sqlite" ? $"Data Source={config.Database};Pooling=False" :
    Environment.GetEnvironmentVariable("ELSA_EFFICIENCY_POSTGRES_CONNECTION") ?? throw new InvalidOperationException("Protected qualification connection is absent.");
Probe.Config = config;
var gate = new Gate(config);
var clock = new OffsetClock(config.Action is "recover" or "reclaim" ? TimeSpan.FromHours(2) : TimeSpan.Zero);
var productionIds = new ServiceCollection().AddWorkflowRuntime().Last(d => d.ServiceType == typeof(IRuntimeExecutionIdGenerator));
await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
{
    services.AddLogging();
    new ActivitiesSequenceFeature().ConfigureServices(services);
    new ActivitiesControlFlowFeature().ConfigureServices(services);
    new Elsa.Events.EventsFeature().ConfigureServices(services);
    new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
    new DispatchWorkflowRuntimeFeature().ConfigureServices(services);
    services.Replace(productionIds);
    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
    services.Replace(ServiceDescriptor.Singleton(new RuntimeExecutionOwnershipOptions { LeaseDuration = TimeSpan.FromHours(1) }));
    services.Replace(ServiceDescriptor.Singleton(new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = TimeSpan.FromHours(1) }));
    services.Configure<WorkflowExecutableGarbageCollectionOptions>(o => o.RootWriteLeaseDuration = TimeSpan.FromHours(1));
    services.AddSingleton<IRuntimeCheckpointCommitEnricher>(gate);
    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
    {
        Provider = config.Provider,
        ConnectionString = connection,
        RecoveryContinuationSigningKey = "private-m4-boundary-recovery-signing-key",
        HierarchyCursorSigningKey = "private-m4-boundary-hierarchy-signing-key"
    }).AddEfModuleMigrations<RuntimeDbContext>(config.Provider);
    if (config.Provider == "Sqlite") services.ConfigureDbContext<RuntimeSqliteDbContext>(b => b.AddInterceptors(gate));
    else services.ConfigureDbContext<RuntimePostgreSqlDbContext>(b => b.AddInterceptors(gate));
    services.AddPersistenceModes(o => { o.Mode = Enum.Parse<PersistenceMode>(config.Mode); o.MaxActivitiesPerCommit = config.Cap; });
    if (config.RetryTransient) services.Replace(ServiceDescriptor.Singleton<IRuntimeDomainRetryPolicy>(new TransientInfrastructureRetryPolicy(Enum.Parse<RuntimeDomainRetryMode>(config.TransientRetryMode))));
    if (config.Point.EndsWith("after-advance", StringComparison.Ordinal))
    {
        var original = services.Last(x => x.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(IRuntimeCheckpointCommitStore), sp => new OuterGate(
            (IRuntimeCheckpointCommitStore)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(sp) ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), gate), original.Lifetime));
    }

}).Build();
foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
if (config.Action == "initialize")
{
    await File.WriteAllTextAsync(config.Result, JsonSerializer.Serialize(new { initialized = true, config.Provider }));
    return;
}
host.InitializeActivityTypes();
var fixedAt = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
WorkflowExecutable Executable(string key, ExecutableNode root, bool wait = false) => new(
    new WorkflowExecutableIdentity("artifact-" + key, "definition-" + key, "version-" + key, "1.0.0", "sha256:" + key), root,
    wait ? new Dictionary<string, WorkflowExecutableResumeTarget> { { ReferenceWorkflows.EventResumeTarget().Id, ReferenceWorkflows.EventResumeTarget().Target } } : new(),
    fixedAt, new Dictionary<string, string>(), new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [],
    incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
var child = Executable("boundary-child", ReferenceWorkflows.Clr("child-leaf", typeof(Probe), []));
var childSource = config.Action is "recover" or "reclaim" ? (await host.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync("boundary-child-source"))! : await host.PublishAsync(child, "boundary-child-source");
var work = ReferenceWorkflows.Sequence("sequence", Enumerable.Range(1, 20).Select(i => ReferenceWorkflows.Clr("work-" + i, typeof(Probe), [])).ToArray());
var edge = ReferenceWorkflows.EventWait();
if (config.Scenario == "child")
{
    var n = ReferenceWorkflows.Clr("dispatch", typeof(Dispatch), [(nameof(Dispatch.WorkflowDefinitionId), child.Identity.DefinitionId), (nameof(Dispatch.WaitForCompletion), false)]);
    var metadata = new Dictionary<string, string>(n.Metadata) { [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(new DispatchWorkflowPin(child.Identity, WorkflowExecutableSourceProvenance.From(childSource)), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
    edge = new ExecutableNode(n.ExecutableNodeId, n.AuthoredActivityId, n.ActivityType, n.ActivityTypeVersion, n.DescriptorType, n.DescriptorPayload, n.InputBindings, metadata, childSlots: n.ChildSlots, structure: n.Structure, activityContract: n.ActivityContract);
}
var parent = Executable("boundary-parent", ReferenceWorkflows.Parallel("root", ("edge", edge), ("work", work)), config.Scenario == "suspend");
var parentSource = config.Action is "recover" or "reclaim" ? (await host.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync("boundary-parent-source"))! : await host.PublishAsync(parent, "boundary-parent-source");
if (config.Action == "reclaim")
{
    // Independent-host root race: after another owner completed the run, delete its terminal history, retire its
    // source references and run a real executable sweep while the stale owner is still paused before its write.
    await using var scope = host.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;
    var deletion = services.GetRequiredService<IWorkflowExecutionDeletionStore>();
    var deletions = new Dictionary<string, string>();
    foreach (var state in await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync())
    {
        var candidate = await deletion.ReadAsync(state.WorkflowExecutionId);
        deletions[state.WorkflowExecutionId] = candidate is null ? "Unread" : (await deletion.TryDeleteTerminalAsync(candidate)).ToString();
    }
    var references = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
    var retired = new Dictionary<string, bool>();
    foreach (var id in new[] { "boundary-parent-source", "boundary-child-source" })
        retired[id] = await references.RetireAsync(id, clock.GetUtcNow(), "runtime-efficiency-root-race");
    var sweep = await services.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
    var remaining = await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync();
    var reclaimed = deletions.Count != 0 && deletions.Values.All(x => x == nameof(WorkflowExecutionDeletionResult.Deleted)) && remaining.Count == 0;
    await File.WriteAllTextAsync(config.Result, JsonSerializer.Serialize(new { correct = reclaimed, deletions, retired, sweep, remainingWorkflows = remaining.Count }, new JsonSerializerOptions { WriteIndented = true }));
    if (!reclaimed) Environment.ExitCode = 2;
    return;
}
if (config.Action != "recover")
    await host.StartPublishedAsync(parentSource, "parent", partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
else
{
    for (var i = 0; i < 12; i++)
        await host.SweepAsync();
}
// Controls and recovery both drive real external delivery and actual bookmark resume.
for (var i = 0; i < 12; i++) await host.SweepAsync();
await using (var scope = host.Services.CreateAsyncScope())
{
    var bookmarks = (await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new BookmarkStatePageQuery("parent", 100))).Items;
    foreach (var bookmark in bookmarks)
        await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
        new BookmarkResumeDispatchRequest("parent", bookmark.StimulusType, bookmark.StimulusHash, JsonSerializer.SerializeToElement(new { eventName = ReferenceWorkflows.EventName })));
}
for (var i = 0; i < 12; i++) await host.SweepAsync();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
    var workflows = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync();
    var activities = new List<ActivityExecutionState>();
    foreach (var workflow in workflows)
        activities.AddRange(await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflow.WorkflowExecutionId));
    var queue = await db.SchedulerWorkItems.CountAsync();
    var pending = await db.RuntimePostCommitOutbox.CountAsync(x => x.Status != (int)RuntimePostCommitOutboxStatus.Delivered);
    var incidents = await db.IncidentStates.CountAsync();
    var correct = workflows.Count == (config.Scenario == "child" ? 2 : 1) && workflows.All(x => x.Status == WorkflowExecutionStatus.Completed) && queue == 0 && pending == 0 && incidents == 0 && activities.Count(x => x.Execution.ExecutableNodeId.StartsWith("work-")) == 20 && activities.All(x => x.Status == ActivityExecutionStatus.Completed) && activities.GroupBy(x => (x.Execution.WorkflowExecutionId, x.Execution.ExecutableNodeId)).All(x => x.Count() == 1);
    await File.WriteAllTextAsync(config.Result, JsonSerializer.Serialize(new { correct, workflows, activities, queue, pending, incidents }, new JsonSerializerOptions { WriteIndented = true }));
    if (!correct)
        Environment.ExitCode = 2;
}

internal sealed record Config(string Action, string Database, string Mode, string Scenario, string Point, string Reached, string Calls, string Result, string? Release = null, int Cap = 50, string Provider = "Sqlite", bool RetryTransient = false, string TransientRetryMode = "RetryAfter");
// Host-owned policy for the database-interruption witness: retry transient provider/socket failures a few times.
internal sealed class TransientInfrastructureRetryPolicy(RuntimeDomainRetryMode mode) : IRuntimeDomainRetryPolicy
{
    public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) =>
        (request.FailureType.StartsWith("Npgsql.", StringComparison.Ordinal) || request.FailureType.StartsWith("System.Net.Sockets.", StringComparison.Ordinal)) && request.FailureCount < 5
            ? new RuntimeDomainRetryDecision(mode, mode == RuntimeDomainRetryMode.RetryAfter ? TimeSpan.FromSeconds(1) : null, "Transient infrastructure failure.", new Dictionary<string, string> { ["policy"] = "runtime-efficiency-transient" })
            : new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.DoNotRetry, null, "Not a transient infrastructure failure.", new Dictionary<string, string> { ["policy"] = "runtime-efficiency-transient" });
}
internal sealed class OffsetClock(TimeSpan offset) : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset; }
namespace Elsa.BoundaryProbe
{
    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    public sealed class Probe : Activity<ActivityUnit>
    {
        internal static Config Config = null!;
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            await File.AppendAllTextAsync(Config.Calls, $"{context.WorkflowExecutionId}|{context.ExecutableNodeId}\n");
            if (context.ExecutableNodeId == "work-3" && Config.Action != "recover" && Config.Point.EndsWith("after-body", StringComparison.Ordinal))
                await Gate.Wait(Config, new { body = context.ExecutableNodeId });
            return ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done);
        }
    }
}
internal sealed class Gate(Config config) : DbTransactionInterceptor, IRuntimeCheckpointCommitEnricher
{
    private readonly ConcurrentDictionary<string, string> targets = new();
    private readonly ConcurrentDictionary<string, byte> committed = new();
    private int reached;
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit c, CancellationToken ct = default) => ValueTask.FromResult(c);
    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit c, CancellationToken ct = default)
    {
        if (c.WorkflowExecutionId == "parent" && (config.Point.StartsWith("parent-", StringComparison.Ordinal)
            ? c.Checkpoint.Name == RuntimeCheckpointNames.ActivityInspectionCaptured &&
              c.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointReason) == "ChildActivityScheduling"
            : (config.Scenario == "suspend" && c.Checkpoint.Name == RuntimeCheckpointNames.ActivitySuspended) ||
              (config.Scenario == "child" && c.StateChanges.WorkflowDispatches.Count != 0)))
            targets.TryAdd(EfRelationalIdentity.Encode(c.CommitId), c.Checkpoint.Name);
        return ValueTask.CompletedTask;
    }
    public async ValueTask After(RuntimeCheckpointCommit c)
    {
        if (config.Action != "recover" && config.Point.EndsWith("after-advance", StringComparison.Ordinal) && committed.ContainsKey(EfRelationalIdentity.Encode(c.CommitId)) && Interlocked.Exchange(ref reached, 1) == 0)
            await Wait(config, new { point = "after-advance", c.CommitId, c.Checkpoint.Name });
    }
    private async Task Hit(DbContext? db, string point)
    {
        if (config.Action == "recover" || config.Point.Replace("parent-", string.Empty, StringComparison.Ordinal) != point || Volatile.Read(ref reached) != 0 || db is null)
            return;
        var marker = db.ChangeTracker.Entries<RuntimeCheckpointCommitEntity>().FirstOrDefault(e => targets.ContainsKey(e.Entity.CommitId));
        if (marker is null || Interlocked.Exchange(ref reached, 1) != 0)
            return;
        await Wait(config, new { point, marker.Entity.CommitId, Name = targets[marker.Entity.CommitId] });
    }
    public static async Task Wait(Config c, object details)
    {
        await File.WriteAllTextAsync(c.Reached, JsonSerializer.Serialize(new { pid = Environment.ProcessId, details }));
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (c.Release is null || !File.Exists(c.Release))
        {
            if (deadline.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Qualification gate was not released within three minutes.");
            await Task.Delay(20);
        }
    }
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction t, TransactionEventData d, InterceptionResult r, CancellationToken ct = default) { await Hit(d.Context, "before-commit"); return r; }
    public override async Task TransactionCommittedAsync(DbTransaction t, TransactionEndEventData d, CancellationToken ct = default)
    {
        if (d.Context is { } context)
            foreach (var entry in context.ChangeTracker.Entries<RuntimeCheckpointCommitEntity>())
                if (targets.ContainsKey(entry.Entity.CommitId)) committed.TryAdd(entry.Entity.CommitId, 0);
        await Hit(d.Context, "after-commit");
    }
}

internal sealed class OuterGate(IRuntimeCheckpointCommitStore inner, Gate gate) : IRuntimeCheckpointCommitStore
{
    public bool SupportsModelCheckpoint => inner.SupportsModelCheckpoint;
    public bool CanCommitModelState(IRuntimeModelExecutionReader r) => inner.CanCommitModelState(r);
    public bool SupportsModelParentAdmission => inner.SupportsModelParentAdmission;
    public bool SupportsModelAdmission => inner.SupportsModelAdmission;
    public bool CanCommitModelAdmission(IRuntimeModelExecutionReader r, IWorkflowExecutionStateStore e, IActivityExecutionStateStore a, IRuntimePostCommitOutboxStore o) => inner.CanCommitModelAdmission(r, e, a, o);
    public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
    public bool CanCommitExactBookmarkResume(IBookmarkStateStore b, IWorkflowExecutionStateStore e, IActivityExecutionStateStore a) => inner.CanCommitExactBookmarkResume(b, e, a);
    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit c, RuntimeCheckpointPersistenceDecision d, CancellationToken ct = default)
    {
        var result = await inner.CommitAsync(c, d, ct);
        await gate.After(c);
        return result;
    }
}
