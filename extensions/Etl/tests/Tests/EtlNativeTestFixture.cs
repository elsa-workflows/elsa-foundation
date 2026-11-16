using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Small real-store harness for native ETL checkpoint and result-delivery boundary tests.</summary>
internal sealed class EtlNativeTestFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T18:00:00Z");

    private readonly string _directory;
    private readonly IHost _host;
    private readonly AsyncServiceScope _lifetimeScope;

    private EtlNativeTestFixture(string directory, IHost host, AsyncServiceScope lifetimeScope,
        InMemoryWorkflowExecutionStateStore workflows, InMemoryActivityExecutionStateStore activities,
        ActivityStateReads activityStateReads,
        InMemoryBookmarkStateStore bookmarks, InMemoryWorkflowExecutableStore executables,
        DispatchRecorder dispatches, EtlScope scope, EtlInvocation invocation,
        IEtlRuntimeStore store, EtlDeliveryLease delivery)
    {
        _directory = directory;
        _host = host;
        _lifetimeScope = lifetimeScope;
        Workflows = workflows;
        Activities = activities;
        ActivityReads = activityStateReads;
        Bookmarks = bookmarks;
        Executables = executables;
        Dispatches = dispatches;
        Scope = scope;
        Invocation = invocation;
        Store = store;
        Delivery = delivery;
    }

    public EtlScope Scope { get; }
    public EtlInvocation Invocation { get; }
    public EtlDeliveryLease Delivery { get; }
    public IEtlRuntimeStore Store { get; }
    public InMemoryWorkflowExecutionStateStore Workflows { get; }
    public InMemoryActivityExecutionStateStore Activities { get; }
    public ActivityStateReads ActivityReads { get; }
    public InMemoryBookmarkStateStore Bookmarks { get; }
    public InMemoryWorkflowExecutableStore Executables { get; }
    public DispatchRecorder Dispatches { get; }
    public IServiceProvider Services => _host.Services;

    public static async Task<EtlNativeTestFixture> CreateAsync(EtlScope? scope = null)
    {
        scope ??= new("tenant-a", "etl-native-partition");
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.NativeBoundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var workflows = new InMemoryWorkflowExecutionStateStore();
        var activities = new InMemoryActivityExecutionStateStore();
        var activityReads = new ActivityStateReads(activities);
        var bookmarks = new InMemoryBookmarkStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var dispatches = new DispatchRecorder();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPersistenceCore(); // The adapter must replace this default with the invocation's partition.
        builder.Services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
        builder.Services.AddSingleton<IActivityExecutionStateStore>(activityReads);
        builder.Services.AddSingleton<IBookmarkStateStore>(bookmarks);
        builder.Services.AddSingleton<IWorkflowExecutableStore>(executables);
        builder.Services.AddSingleton(dispatches);
        builder.Services.AddScoped<IBookmarkResumeDispatcher, RecordingBookmarkResumeDispatcher>();
        builder.Services.AddScoped<EtlNativeResultExecutor>();
        builder.Services.AddScoped<IEtlResultDelivery, EtlNativeResultDelivery>();
        new EtlRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = $"Data Source={Path.Combine(directory, "runtime.db")};Pooling=False"
        }.ConfigureServices(builder.Services);
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);

        var host = builder.Build();
        AsyncServiceScope? lifetimeScope = null;
        var hostStarted = false;
        try
        {
            await host.StartAsync();
            hostStarted = true;
            lifetimeScope = host.Services.CreateAsyncScope();
            var store = lifetimeScope.Value.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            await lifetimeScope.Value.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Database.MigrateAsync();
            var activity = EtlNativeCheckpointTests.Activity(scope);
            var invocation = EtlNativeBoundary.Invocation(activity);
            var parent = new WorkflowExecutionState(
                invocation.ResultAddress.WorkflowExecutionId,
                new WorkflowExecutableIdentity(invocation.ResultAddress.ExecutableId, "definition-1", "version-1", "1",
                    invocation.ResultAddress.ExecutableHash),
                WorkflowExecutionStatus.Running,
                null,
                Now,
                Now,
                null,
                null,
                null,
                null,
                scope.TenantId,
                new Dictionary<string, string>())
            {
                Partition = new WorkflowExecutionPartition(scope.PersistencePartition)
            };
            await workflows.SaveAsync(parent);
            await activities.SaveAsync(activity);
            await bookmarks.SaveAsync(EtlNativeCheckpointTests.Bookmark(activity));

            var cancellation = await store.CancelAsync(invocation);
            if (cancellation.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.Terminal))
                throw new InvalidOperationException("The native ETL test run could not be durably seeded.");
            var delivery = await store.ClaimDeliveryAsync(scope, "native-boundary-test")
                ?? throw new InvalidOperationException("The native ETL test run did not produce an owed terminal delivery.");

            return new(directory, host, lifetimeScope.Value, workflows, activities, activityReads, bookmarks, executables,
                dispatches, scope, invocation, store, delivery);
        }
        catch
        {
            if (lifetimeScope is { } failedScope)
                await failedScope.DisposeAsync();
            if (hostStarted)
                await host.StopAsync();
            host.Dispose();
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public async Task<EtlDeliveryDisposition?> DeliverAsync(CancellationToken cancellationToken = default)
        => await DeliverAsync(Delivery, cancellationToken);

    public async Task<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEtlResultDelivery>()
            .DeliverAsync(delivery, cancellationToken);
    }

    public async Task<EtlRuntimeOutboxRecord> ReadOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Outbox.AsNoTracking()
            .SingleAsync(row => row.DeliveryId == Delivery.DeliveryId);
    }

    public async ValueTask<PersistenceOperationScope> OpenNativeScopeAsync(CancellationToken cancellationToken = default) =>
        await Services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Scope.PersistencePartition), cancellationToken);

    /// <summary>Adds the pinned published node used by direct RunEtl activity execution tests.</summary>
    public async Task SetExecutableAsync(string? pin = null, string? literalToken = null,
        Func<WorkflowExecutableResumeTarget, IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>>? resumeTargets = null)
    {
        pin ??= JsonSerializer.Serialize(Invocation.Artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        literalToken ??= new EtlPublishedSelection(Invocation.Artifact.DefinitionId,
            Invocation.Artifact.DefinitionVersion, Invocation.Artifact.ContentHash).ToToken();
        var address = Invocation.ResultAddress;
        var inputType = new ValueTypeDescriptor("String");
        var pipelineBinding = new RuntimeInputBinding(nameof(RunEtl.Pipeline), inputType,
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(inputType, JsonSerializer.SerializeToElement(literalToken), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode(address.NodeId, "authored-etl", RunEtl.ActivityType, "1.0.0",
            "test.clr-activity", JsonSerializer.SerializeToElement(new { type = RunEtl.ActivityType }),
            new Dictionary<string, RuntimeInputBinding> { [nameof(RunEtl.Pipeline)] = pipelineBinding },
            new Dictionary<string, string> { [EtlNativeBoundary.PinKey] = pin });
        var target = new WorkflowExecutableResumeTarget(address.ResumeTargetId, address.NodeId, "ResumeAsync",
            new Dictionary<string, string>(), EtlNativeBoundary.ResumeTargetKey);
        var parent = await Workflows.FindAsync(address.WorkflowExecutionId)
            ?? throw new InvalidOperationException("The fixture parent state is missing.");
        var executable = new WorkflowExecutable(parent.PinnedExecutable, node,
            resumeTargets?.Invoke(target) ?? new Dictionary<string, WorkflowExecutableResumeTarget> { [target.ResumeTargetId] = target },
            Now, new Dictionary<string, string>(), new IncidentStrategyReference("ContinueWithIncidents", "1"));
        await Executables.SaveAsync(executable);
    }

    public BookmarkResumeDispatchResult DispatchResult(BookmarkResumeDispatchStatus status)
    {
        var address = Invocation.ResultAddress;
        if (status != BookmarkResumeDispatchStatus.Dispatched)
            return new(status, address.WorkflowExecutionId, reason: "test dispatch outcome");

        var bookmark = EtlNativeCheckpointTests.Bookmark(EtlNativeCheckpointTests.Activity(Scope));
        var descriptor = JsonSerializer.SerializeToElement(new { type = RunEtl.ActivityType });
        var node = new ExecutableNode(address.NodeId, "authored-etl", RunEtl.ActivityType, "1.0.0",
            "test.clr-activity", descriptor, new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var target = new WorkflowExecutableResumeTarget(address.ResumeTargetId, address.NodeId, "test.etl-resume",
            new Dictionary<string, string>());
        var resolution = new BookmarkResumeResolution(bookmark, node, target, null);
        var command = new WorkflowExecutionCommandDispatchResult("native-envelope", address.WorkflowExecutionId,
            WorkflowExecutionCommandDispatchStatus.Accepted, Now);
        var agent = new WorkflowExecutionActorDescriptor(address.WorkflowExecutionId, "native-agent", "in-process",
            WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, Now);
        return new(status, address.WorkflowExecutionId, bookmark, resolution, command, agent);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        await _lifetimeScope.DisposeAsync();
        _host.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    internal sealed class DispatchRecorder
    {
        private readonly object _gate = new();
        private readonly List<BookmarkResumeDispatchRequest> _requests = [];
        private Func<BookmarkResumeDispatchRequest, CancellationToken, ValueTask<BookmarkResumeDispatchResult>>? _respond;

        public Func<BookmarkResumeDispatchRequest, CancellationToken, ValueTask<BookmarkResumeDispatchResult>> Respond
        {
            get => Volatile.Read(ref _respond) ?? ((_, _) => ValueTask.FromException<BookmarkResumeDispatchResult>(
                new InvalidOperationException("No test dispatch response was configured.")));
            set => Volatile.Write(ref _respond, value);
        }

        public IReadOnlyList<BookmarkResumeDispatchRequest> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        public PersistenceScope? ObservedScope { get; private set; }
        public BookmarkResumeDispatchResult? LastResult { get; private set; }

        public void Record(BookmarkResumeDispatchRequest request, PersistenceScope scope)
        {
            lock (_gate)
            {
                _requests.Add(request);
                ObservedScope = scope;
            }
        }

        public void RecordResult(BookmarkResumeDispatchResult result) => LastResult = result;
    }

    internal sealed class ActivityStateReads(InMemoryActivityExecutionStateStore inner) : IActivityExecutionStateStore
    {
        private int _findCalls;
        private int _hide;

        public int FindCalls => Volatile.Read(ref _findCalls);
        public bool HideActivities
        {
            get => Volatile.Read(ref _hide) != 0;
            set => Volatile.Write(ref _hide, value ? 1 : 0);
        }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _findCalls);
            return HideActivities
                ? ValueTask.FromResult<ActivityExecutionState?>(null)
                : inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.CountAsync(workflowExecutionId, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query,
            CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query,
            CancellationToken cancellationToken = default) => inner.ListByParentPageAsync(query, cancellationToken);
    }

    private sealed class RecordingBookmarkResumeDispatcher(DispatchRecorder recorder, IPersistenceAccessContextAccessor access)
        : IBookmarkResumeDispatcher
    {
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recorder.Record(request, access.Current.RequireScope());
            var result = await recorder.Respond(request, cancellationToken);
            recorder.RecordResult(result);
            return result;
        }
    }
}
