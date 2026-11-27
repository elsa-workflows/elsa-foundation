using System.Data.Common;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Values;
using Kernel.Foundation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>A kernel shell's Runs list: the kernel's runs in the host's tenant, through the runtime API's own list.</summary>
public sealed class KernelRunListTests
{
    private const string Tenant = "default";
    private static readonly BoundScope Scope = EmbeddedTenant.Bind(Tenant);

    /// <summary>The foundation's reads over fixed views, two to a page, so a list reads every page.</summary>
    private sealed class Reads(params ActorView[] views) : IActorReads, IActorSequencedJournalReads
    {
        public List<ActorQuery> Queries { get; } = [];
        public List<(BoundScope Scope, ActorRef Actor)> Gets { get; } = [];
        public List<(BoundScope Scope, ActorRef Actor)> Tails { get; } = [];
        public Func<ActorRef, ActorView?>? PointResult { get; init; }
        public Action? ReadTail { get; set; }
        public JournalCancellation? Cancellation { get; set; }
        public IReadOnlyDictionary<string, JournalEntry[]> Journal { get; init; } = new Dictionary<string, JournalEntry[]>();
        public List<SequencedJournalEntry>? Sequence { get; init; }
        public Func<ActorSequencedJournalPage, ActorSequencedJournalPage>? SequencePage { get; set; }
        public int SequenceReads { get; private set; }
        public bool SupportsSequencedJournal => Sequence is not null;

        public ValueTask<ActorSequencedJournalPage?> ReadSequencedJournalAsync(BoundScope scope, ActorRef actor, long afterSequence,
            long? upperSequence, int limit, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Tenant, scope.Tenant);
            Assert.Contains(views, view => view.Header.Ref == actor);
            SequenceReads++;
            var sequence = Assert.IsType<List<SequencedJournalEntry>>(Sequence);
            var upper = upperSequence ?? sequence.Count;
            var items = sequence.Where(entry => entry.Sequence > afterSequence && entry.Sequence <= upper).Take(limit).ToArray();
            var page = new ActorSequencedJournalPage(items, upper == 0 ? 0 : 1, upper,
                items.LastOrDefault()?.Sequence ?? afterSequence, sequence.LastOrDefault(entry => entry.Sequence == upper)?.RecordedAt,
                CapturedFromStart: true, AsOf: 3_000);
            return ValueTask.FromResult<ActorSequencedJournalPage?>(SequencePage is { } project ? project(page) : page);
        }

        public ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
        {
            Gets.Add((scope, actor));
            return ValueTask.FromResult(PointResult is { } get ? get(actor) : views.FirstOrDefault(view => view.Header.Ref == actor));
        }

        public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var matching = views.Where(view => view.Header.Type == query.Type &&
                (query.Labels is null || query.Labels.All(label => view.Header.Labels.GetValueOrDefault(label.Key) == label.Value))).ToArray();
            var skip = from is { } token ? int.Parse(token.Value) : 0;
            var page = matching.Skip(skip).Take(2).ToArray();
            return ValueTask.FromResult(new Page<ActorView>(page, skip + 2 < matching.Length ? new PageToken((skip + 2).ToString()) : null, 1_000));
        }

        public ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default)
        {
            Tails.Add((scope, actor));
            var skip = from is { } token ? int.Parse(token.Value) : 0;
            var entries = Journal.GetValueOrDefault(actor.Id) ?? [];
            var items = entries.Skip(skip).Take(2).ToArray();
            var cancellation = Cancellation;
            ReadTail?.Invoke();
            return ValueTask.FromResult(new Page<JournalEntry>(items, items.Length == 0 ? null : new PageToken((skip + items.Length).ToString()), 1_000)
                { JournalHeadRevision = entries.LastOrDefault()?.Revision, JournalCancellation = cancellation });
        }

        public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static ActorView Run(string id, ActorStatus status, string workflow, string definition, long? startedAt, long? endedAt = null,
        bool suspended = false, string? subStatus = null, Kernel.Foundation.Contracts.History history = Kernel.Foundation.Contracts.History.Full)
    {
        var typeData = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Model"] = "native", ["Workflow"] = workflow, ["Version"] = 14, ["Suspended"] = suspended, ["SubStatus"] = subStatus,
            ["Ephemeral"] = false, ["History"] = history, ["Definition"] = definition == workflow ? null : definition, ["StartedAt"] = startedAt
        });
        return new ActorView(new ActorHeader(new ActorRef(id), Kernel.WorkflowActor.WorkflowActor.ActorType, Tenant, status, null, null, endedAt,
            typeData, new Dictionary<string, string> { ["definition"] = definition }), null, null, 1_000);
    }

    private static readonly ActorView[] Views =
    [
        Run("run-1", ActorStatus.Finished, "orders@1", "orders", 1_000, 1_500),
        Run("run-2", ActorStatus.Running, "orders@1", "orders", 2_000),
        Run("run-3", ActorStatus.Running, "orders@1", "orders", 3_000, suspended: true),
        Run("run-4", ActorStatus.Faulted, "invoices@2", "invoices", 4_000, 4_100, subStatus: "native.burst.limit"),
        Run("run-5", ActorStatus.Cancelled, "invoices@2", "invoices", 5_000, 5_200),
    ];

    /// <summary>A kernel shell: the runtime API's default list beside the host's kernel runs, with the feature applied.</summary>
    private static ServiceProvider Shell(Reads reads, PersistenceAccessContext access, IActivityInspectionContextAsync? authorization = null,
        IWorkflowRuntimeOperatorAccess? operatorAccess = null)
    {
        var services = new ServiceCollection();
        var executables = new InMemoryWorkflowExecutableStore();
        services.AddSingleton<IWorkflowExecutableStore>(executables);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(access));
        services.AddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.AddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.AddSingleton<IActivityInspectionContextAsync>(authorization ?? new AllowAllActivityExecutionInspectionAuthorizationContext());
        if (operatorAccess is not null) services.AddSingleton(operatorAccess);
        services.AddSingleton(new KernelRuns(Scope, _ => ValueTask.FromResult<IActorReads>(reads)));
        // The runtime API's default reads.
        services.AddScoped<IWorkflowInstanceListService, WorkflowInstanceListService>();
        services.AddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.AddSingleton<IActivityExecutionInspectionStore, InMemoryActivityExecutionInspectionStore>();
        services.AddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.AddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
        services.AddSingleton(new RuntimePersistenceModeInspector([new PersistenceModeOptions()]));
        services.AddScoped<WorkflowInstanceDetailsService>();
        services.AddScoped<ActivityExecutionInspectionService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RuntimeHistoryInspectionService>();
        services.AddScoped<IWorkflowInstanceDetailsService, WorkflowInstanceDetailsService>();
        var feature = new WorkflowsRuntimeKernelApiFeature();
        feature.ConfigureServices(services);
        feature.PostConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceListView> ListAsync(ServiceProvider shell, ListWorkflowInstances request) =>
        shell.CreateScope().ServiceProvider.GetRequiredService<IWorkflowInstanceListService>().ListAsync(request, CancellationToken.None);

    private static readonly PersistenceAccessContext HostTenant = PersistenceAccessContext.Scoped(new PersistenceScope(Tenant));

    [Fact]
    public async Task Standard_history_pages_committed_structure_and_catches_up_without_reopening_the_snapshot()
    {
        const string node = "branch:leaf";
        var root = new Native.Node("parent", typeof(Native.Sequence));
        root.Children.Add(new Native.Node(node, typeof(HistoryDiagnostic)));
        var workflow = new Native.Workflow("structural-history", root);
        var typed = await NativeTurn.StartAsync(workflow, new() { UseStructuralJournal = true });
        var legacy = await NativeTurn.StartAsync(workflow);
        Assert.All(legacy.Recorded, entry => Assert.Equal(JsonValueKind.String, entry.ValueKind));
        var legacyStart = Assert.Single(legacy.Recorded, entry => entry.GetString() == "b:f2:branch:leaf:f1");
        var genuine = typed.Recorded.Where(entry => entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty("Node", out var path) && path.GetString() == node).ToArray();
        Assert.Equal(2, genuine.Length);
        var diagnostic = typed.Recorded.Where(entry => entry.ValueKind == JsonValueKind.String &&
            entry.GetString()!.Contains("private-diagnostic", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, diagnostic.Length);
        var reads = new Reads(Views)
        {
            Journal = new Dictionary<string, JournalEntry[]>
            {
                ["run-1"] = typed.Recorded.Select(entry => new JournalEntry(1, entry.GetRawText())).ToArray()
            },
            Sequence =
            [
                new(1, 1, 0, "\"start\"", 1_200),
                new(2, 1, 1, diagnostic[0].GetRawText(), 1_200),
                new(3, 1, 2, diagnostic[1].GetRawText(), 1_200),
                new(4, 1, 3, legacyStart.GetRawText(), 1_200),
                new(5, 1, 4, genuine[0].GetRawText(), 1_200),
                new(6, 1, 5, "{\"value\":\"private\"}", 1_200)
            ]
        };
        var authorization = new InspectionAccess(structure: true, values: false);
        using var shell = Shell(reads, HostTenant, authorization);
        using var scope = shell.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>();
        var endpoint = new Elsa.Workflows.Runtime.Api.Endpoints.History.Page.Endpoint(service);
        var request = new GetRuntimeHistory("run-1", Limit: 4, ActivityExecutionId: "f2");
        var first = await endpoint.HandleAsync(request, CancellationToken.None);
        Assert.Empty(first.Items);
        Assert.Equal(6, first.UpperSequence);
        Assert.False(first.Coverage.CapturedFromStart);
        Assert.True(first.Coverage.HasGaps);
        Assert.NotNull(first.NextContinuationToken);
        Assert.NotNull(first.CatchUpToken);
        reads.Sequence!.Add(new(7, 2, 0, genuine[1].GetRawText(), 2_000));
        var frozen = await endpoint.HandleAsync(request with { Cursor = first.NextContinuationToken }, CancellationToken.None);
        var started = Assert.Single(frozen.Items);
        Assert.Equal(("ActivityStarted", node, "f1", 5L),
            (started.Name, started.ExecutableNodeId, started.ParentActivityExecutionId, started.Sequence));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(typed.Now), started.OccurredAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_200), started.RecordedAt);
        Assert.Equal(6, frozen.ScannedThroughSequence);
        Assert.Null(frozen.NextContinuationToken);
        var live = await endpoint.HandleAsync(request with { Cursor = first.CatchUpToken }, CancellationToken.None);
        var completed = Assert.Single(live.Items);
        Assert.Equal(("ActivityCompleted", 7L), (completed.Name, completed.Sequence));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(typed.Now), completed.OccurredAt);
        await Assert.ThrowsAsync<ArgumentException>(() => endpoint.HandleAsync(
            request with { Cursor = first.CatchUpToken, ActivityExecutionId = "different" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => endpoint.HandleAsync(
            request with { Cursor = first.CatchUpToken, WorkflowExecutionId = "run-2" }, CancellationToken.None));

        using var foreign = Shell(reads, PersistenceAccessContext.Scoped(new PersistenceScope("foreign")));
        using var foreignScope = foreign.CreateScope();
        Assert.Null(await foreignScope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>().ReadAsync(request));
        Assert.Equal(3, reads.SequenceReads);

        var browse = scope.ServiceProvider.GetRequiredService<IActivityExecutionBrowseSource>();
        var run = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        var timeline = (await run.BrowseAsync(new("run-1", 10), null, CancellationToken.None))!;
        Assert.Equal(2, timeline.Page.TotalCount);
        Assert.All(timeline.Page.Items, item => Assert.Equal(ActivityExecutionStatus.Completed, item.Execution.Status));
    }

    [Theory]
    [InlineData("""{"NativeStructure":2}""")]
    [InlineData("""{"NativeStructure":1,"Kind":2,"At":1000,"Frame":42,"Node":"branch"}""")]
    [InlineData("""{"NativeStructure":1,"Kind":0,"At":9223372036854775807}""")]
    public async Task Standard_history_stops_at_a_malformed_structural_marker_and_timeline_ignores_legacy_strings_in_its_turn(string marker)
    {
        var reads = new Reads(Views)
        {
            Sequence = [new(1, 1, 0, HistoryStart, 1_200), new(2, 2, 0, marker, 2_000)],
            Journal = new Dictionary<string, JournalEntry[]>
            {
                ["run-1"] = [new(1, HistoryStart), new(2, marker), new(2, "\"t:2000\""), new(2, "\"b:f2:lookalike:f1\"")]
            }
        };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var endpoint = new Elsa.Workflows.Runtime.Api.Endpoints.History.Page.Endpoint(
            scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>());
        AssertInvalidHistoryPage(await endpoint.HandleAsync(new("run-1"), CancellationToken.None));

        var browse = scope.ServiceProvider.GetRequiredService<IActivityExecutionBrowseSource>();
        var run = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        var timeline = (await run.BrowseAsync(new("run-1", 10), null, CancellationToken.None))!;
        Assert.Equal("f1", Assert.Single(timeline.Page.Items).Execution.ActivityExecutionId);
    }

    [Fact]
    public async Task Standard_history_rejects_a_reused_commit_ordinal_on_a_frozen_page()
    {
        var reads = new Reads(Views)
        {
            Sequence = [new(1, 1, 0, HistoryStart, 1_200), new(2, 1, 0, HistoryStart, 1_200)]
        };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>();
        var request = new GetRuntimeHistory("run-1", Limit: 1);
        var first = Assert.IsType<RuntimeHistoryPageView>(await history.ReadAsync(request));
        Assert.Single(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        AssertInvalidHistoryPage(Assert.IsType<RuntimeHistoryPageView>(
            await history.ReadAsync(request with { Cursor = first.NextContinuationToken })));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Standard_history_refuses_an_unrepresentable_page_timestamp(bool asOf)
    {
        var reads = new Reads(Views)
        {
            Sequence = [new(1, 1, 0, HistoryStart, 1_200)],
            SequencePage = page => asOf ? page with { AsOf = long.MinValue } : page with { LastRecordedAt = long.MaxValue }
        };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>();
        AssertInvalidHistoryPage(Assert.IsType<RuntimeHistoryPageView>(await history.ReadAsync(new("run-1"))));
    }

    [Fact]
    public async Task Standard_history_normalizes_only_transient_provider_read_failures()
    {
        var reads = new Reads(Views) { Sequence = [] };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>();
        var request = new GetRuntimeHistory("run-1");

        foreach (var transient in new Exception[] { new TimeoutException(), new JournalDbException(transient: true) })
        {
            reads.SequencePage = _ => throw transient;
            var unavailable = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => history.ReadAsync(request));
            Assert.Same(transient, unavailable.InnerException);
        }

        reads.SequencePage = _ => throw new JournalDbException(transient: false);
        await Assert.ThrowsAsync<JournalDbException>(() => history.ReadAsync(request));
        reads.SequencePage = _ => throw new ArgumentOutOfRangeException("afterSequence");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => history.ReadAsync(request));
        reads.SequencePage = _ => throw new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => history.ReadAsync(request));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Standard_history_normalizes_transient_run_lookup_failures_before_and_after_reading(int failingLookup)
    {
        Exception failure = failingLookup == 1 ? new TimeoutException() : new JournalDbException(transient: true);
        var lookupCount = 0;
        var reads = new Reads(Views)
        {
            Sequence = [new(1, 1, 0, HistoryStart, 1_200)],
            PointResult = _ => ++lookupCount == failingLookup ? throw failure : Views[0]
        };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryPageReader>();

        var unavailable = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => history.ReadAsync(new("run-1")));
        Assert.Same(failure, unavailable.InnerException);
        Assert.Equal("read-run", unavailable.Operation);
        Assert.Equal(failingLookup, lookupCount);
        Assert.Equal(failingLookup - 1, reads.SequenceReads);
    }

    private const string HistoryStart = """{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f1","Node":"branch"}""";

    private static void AssertInvalidHistoryPage(RuntimeHistoryPageView page)
    {
        Assert.Equal("invalid", page.Coverage.State);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.ScannedThroughSequence);
        Assert.Null(page.NextContinuationToken);
        Assert.Null(page.CatchUpToken);
    }

    private sealed class JournalDbException(bool transient) : DbException
    {
        public override bool IsTransient => transient;
    }

    public sealed class HistoryDiagnostic : Native.CompositeActivity
    {
        protected override void Execute(Native.CompositeContext context)
        {
            context.Record($"f:{context.FrameId}:private-diagnostic");
            context.Record(JsonSerializer.Serialize(new
            {
                NativeStructure = 1, Kind = 4, At = 1000, Frame = context.FrameId, Node = "private-diagnostic", Parent = "f1"
            }));
            context.Complete();
        }

        protected override void OnChildCompleted(Native.CompositeContext context, Native.ChildCompleted child) =>
            throw new InvalidOperationException("The diagnostic fixture has no children.");
    }

    [Fact]
    public async Task Details_read_one_committed_run_and_preserve_its_summary_and_permissions()
    {
        var reads = new Reads(Views);
        var authorization = new InspectionAccess(structure: true, values: false);
        var management = new ManageAccess();
        using var shell = Shell(reads, HostTenant, authorization, management);
        var executables = (InMemoryWorkflowExecutableStore)shell.GetRequiredService<IWorkflowExecutableStore>();
        var executable = Compiled.ExecutableOf("orders@1", "orders", Compiled.Sequence("root", Compiled.WriteLine("w", Compiled.Literal("text", "hi"))));
        await executables.SaveAsync(executable);
        using var request = shell.CreateScope();
        var details = request.ServiceProvider.GetRequiredService<IWorkflowInstanceDetailsService>();

        var view = Assert.IsType<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView>(
            await details.GetAsync(new("run-1"), CancellationToken.None));

        Assert.Equal((Scope, new ActorRef("run-1")), Assert.Single(reads.Gets));
        Assert.Empty(reads.Queries);
        Assert.Empty(reads.Tails);
        Assert.Equal(("run-1", "Completed", executable.Identity.ArtifactHash, Tenant),
            (view.Instance.WorkflowExecutionId, view.Instance.Status, view.Instance.ArtifactHash, view.Instance.TenantId));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_000), view.Instance.StartedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_500), view.Instance.CompletedAt);
        Assert.Empty(view.Activities);
        Assert.True(view.ActivitiesDeferred);
        Assert.Empty(view.Incidents);
        Assert.Empty(view.Outputs);
        Assert.Null(view.ActivityNextContinuationToken);
        Assert.Equal(("Balanced", "boundary-level"), (view.PersistenceMode, view.InspectionGranularity));
        Assert.False(view.Permissions!.CanManage);
        Assert.False(view.Permissions.CanInspectValues);
        Assert.Equal(["run-1"], authorization.StructureChecks);
        Assert.Equal(["run-1"], authorization.ValueChecks);
        Assert.Empty(management.Checks);
    }

    [Fact]
    public async Task Details_make_missing_wrong_scope_wrong_identity_and_hidden_runs_indistinguishable()
    {
        var run = Views[0];
        var reads = new Reads { PointResult = reference => reference.Id switch
        {
            "wrong-id" => run,
            "wrong-tenant" => run with { Header = run.Header with { Ref = reference, Tenant = "other" } },
            "wrong-type" => run with { Header = run.Header with { Ref = reference, Type = "schedule" } },
            "run-1" => run,
            _ => null
        } };
        var authorization = new InspectionAccess(structure: false, values: true);
        var management = new ManageAccess();
        using var shell = Shell(reads, HostTenant, authorization, management);
        using var request = shell.CreateScope();
        var details = request.ServiceProvider.GetRequiredService<IWorkflowInstanceDetailsService>();
        foreach (var id in new[] { "missing", "wrong-id", "wrong-tenant", "wrong-type", "run-1" })
            Assert.Null(await details.GetAsync(new(id), CancellationToken.None));
        Assert.Equal(["run-1"], authorization.StructureChecks);
        Assert.Empty(authorization.ValueChecks);
        Assert.Empty(management.Checks);
        Assert.Empty(reads.Tails);

        reads.Gets.Clear();
        using var other = Shell(reads, PersistenceAccessContext.Scoped(new PersistenceScope("other")));
        using var across = Shell(reads, PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("kernel-run-detail-test")));
        foreach (var denied in new[] { other, across })
        {
            using var deniedRequest = denied.CreateScope();
            Assert.Null(await deniedRequest.ServiceProvider.GetRequiredService<IWorkflowInstanceDetailsService>()
                .GetAsync(new("run-1"), CancellationToken.None));
        }
        Assert.Empty(reads.Gets);
    }

    [Fact]
    public async Task Browse_and_point_project_one_exact_native_timeline_across_journal_pages()
    {
        var entries = new JournalEntry[]
        {
            new(1, "\"c:old:before-upgrade\""),
            new(2, "\"t:1000\""),
            new(2, "\"b:f1:root:\""),
            new(2, "\"a:f2:1\""),
            new(3, "\"t:1100\""),
            new(3, "\"b:f2:child:with-colon:f1\""),
            new(4, "\"t:900\""),
            new(4, "\"b:f3:other:f1\""),
            new(5, "\"t:1300\""),
            new(5, "\"f:f2:child:with-colon\""),
            new(5, "\"x:f3:other\""),
            new(5, "\"c:f1:root\"")
        };
        var journal = new Dictionary<string, JournalEntry[]> { ["run-1"] = entries, ["minimal"] = entries };
        var reads = new Reads(Run("run-1", ActorStatus.Running, "orders@1", "orders", 1_000),
            Run("minimal", ActorStatus.Finished, "orders@1", "orders", 1_000, 1_500,
            history: Kernel.Foundation.Contracts.History.Minimal))
        {
            Journal = journal
        };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var services = scope.ServiceProvider;
        var browse = services.GetRequiredService<IActivityExecutionBrowseSource>();
        var run = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        // A later turn commits between the first and second physical reads; it must not enter the first browse or its cursor pages.
        reads.ReadTail = () =>
        {
            journal["run-1"] = [.. entries, new(6, "\"t:1400\""), new(6, "\"b:f4:later:f1\"")];
            reads.ReadTail = null;
        };
        var first = (await run.BrowseAsync(new("run-1", 1, offset: 0), null, CancellationToken.None))!;
        Assert.Equal(5, first.JournalHeadRevision);
        Assert.Equal(3, first.Page.TotalCount);
        Assert.Equal("f1", Assert.Single(first.Page.Items).Execution.ActivityExecutionId);
        var next = (await run.BrowseAsync(new("run-1", 2, offset: 1), first.JournalHeadRevision, CancellationToken.None))!;
        Assert.Equal(3, next.Page.TotalCount);
        Assert.Equal(["f2", "f3"], next.Page.Items.Select(item => item.Execution.ActivityExecutionId));
        var refreshed = (await run.BrowseAsync(new("run-1", 10), null, CancellationToken.None))!;
        Assert.Equal(6, refreshed.JournalHeadRevision);
        Assert.Equal(4, refreshed.Page.TotalCount);

        var children = (await run.BrowseAsync(new("run-1", 10, ancestorActivityExecutionId: "f1"), first.JournalHeadRevision,
            CancellationToken.None))!.Page;
        Assert.Equal(["f2", "f3"], children.Items.Select(item => item.Execution.ActivityExecutionId));
        Assert.Equal(["Faulted", "Cancelled"], children.Items.Select(item => item.Execution.Status.ToString()));
        Assert.Equal(900L, children.Items[1].Execution.StartedAt!.Value.ToUnixTimeMilliseconds());
        Assert.All(children.Items, item => Assert.Equal("f1", item.Execution.Provenance.ParentActivityExecutionId));
        Assert.Null(await run.BrowseAsync(new("other-run", 10), null, CancellationToken.None));

        var point = services.GetRequiredService<IActivityExecutionInspectionService>();
        reads.Tails.Clear();
        var child = (await point.GetAsync(new("run-1", "f2"), CancellationToken.None))!;
        Assert.Equal("child:with-colon", child.ExecutableNodeId);
        Assert.Equal((1_000L, 1_100L, 1_300L), (child.ScheduledAt.ToUnixTimeMilliseconds(),
            child.StartedAt!.Value.ToUnixTimeMilliseconds(), child.CompletedAt!.Value.ToUnixTimeMilliseconds()));
        Assert.Equal(5, reads.Tails.Count);
        Assert.Null(await point.GetAsync(new("run-1", "old"), CancellationToken.None));
        var minimal = (await browse.FindWorkflowAsync("minimal", CancellationToken.None))!;
        Assert.Empty((await minimal.BrowseAsync(new("minimal", 10), null, CancellationToken.None))!.Page.Items);
        Assert.All(reads.Tails, tail => Assert.Equal((Scope, new ActorRef("run-1")), tail));
    }

    [Fact]
    public async Task Browse_discards_a_journal_when_its_actor_is_retired_between_pages()
    {
        var retired = false;
        var reads = new Reads
        {
            PointResult = _ => retired ? null : Views[0],
            Journal = new Dictionary<string, JournalEntry[]> { ["run-1"] = [new(1, "\"t:1000\""), new(1, "\"b:f1:root:\"")] },
            ReadTail = () => retired = true
        };
        using var shell = Shell(reads, HostTenant);
        using var request = shell.CreateScope();
        var browse = request.ServiceProvider.GetRequiredService<IActivityExecutionBrowseSource>();
        var run = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        Assert.Null(await run.BrowseAsync(new("run-1", 10), null, CancellationToken.None));
        Assert.Equal(2, reads.Gets.Count);
    }

    [Fact]
    public async Task A_retained_operator_cancellation_closes_only_the_running_frames_in_its_snapshot()
    {
        var journal = new Dictionary<string, JournalEntry[]>
        {
            ["run-1"] = [
                new(1, "\"t:1000\""), new(1, "\"b:f1:root:\""), new(1, "\"b:f2:completed:f1\""), new(1, "\"c:f2:completed\""),
                new(2, """{"Outcome":"parked"}""")]
        };
        var view = Run("run-1", ActorStatus.Faulted, "orders@1", "orders", 1_000);
        var reads = new Reads { PointResult = _ => view, Journal = journal };
        using var shell = Shell(reads, HostTenant);
        using var scope = shell.CreateScope();
        var browse = scope.ServiceProvider.GetRequiredService<IActivityExecutionBrowseSource>();
        var first = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        var before = (await first.BrowseAsync(new("run-1", 1, offset: 0), null, CancellationToken.None))!;
        Assert.Equal(2, before.JournalHeadRevision);
        Assert.Equal(ActivityExecutionStatus.Running, Assert.Single(before.Page.Items).Execution.Status);

        journal["run-1"] = [.. journal["run-1"], new(3, """{"Outcome":"protected","Disposition":"terminated","Operation":"fixture","Cleared":1}""")];
        view = view with { Header = view.Header with { Status = ActorStatus.Cancelled, EndedAt = 1_300 } };
        reads.Cancellation = new JournalCancellation(3, 1_300);
        var retained = (await browse.FindWorkflowAsync("run-1", CancellationToken.None))!;
        var previous = (await retained.BrowseAsync(new("run-1", 1, offset: 0), before.JournalHeadRevision, CancellationToken.None))!;
        Assert.Equal(ActivityExecutionStatus.Running, Assert.Single(previous.Page.Items).Execution.Status);
        var current = (await retained.BrowseAsync(new("run-1", 10), null, CancellationToken.None))!;
        Assert.Equal(3, current.JournalHeadRevision);
        Assert.Collection(current.Page.Items, root =>
        {
            Assert.Equal(ActivityExecutionStatus.Cancelled, root.Execution.Status);
            Assert.Equal(1_300L, root.Execution.CompletedAt!.Value.ToUnixTimeMilliseconds());
        }, child =>
        {
            Assert.Equal(ActivityExecutionStatus.Completed, child.Execution.Status);
            Assert.Equal(1_000L, child.Execution.CompletedAt!.Value.ToUnixTimeMilliseconds());
        });
    }

    [Fact]
    public async Task Details_fall_back_only_to_authorized_legacy_authoring_test_runs()
    {
        var reads = new Reads(Views[0], Views[0] with { Header = Views[0].Header with { Ref = new ActorRef("schedule"), Type = "schedule" } });
        var management = new ManageAccess();
        using var shell = Shell(reads, HostTenant, operatorAccess: management);
        var legacy = shell.GetRequiredService<IWorkflowExecutionStateStore>();
        await legacy.SaveAsync(Legacy("test", Tenant, WorkflowRunKind.TestRun));
        await legacy.SaveAsync(Legacy("old-published", Tenant, WorkflowRunKind.PublishedRun));
        await legacy.SaveAsync(Legacy("other-tenant", "other", WorkflowRunKind.TestRun));
        await legacy.SaveAsync(Legacy("schedule", Tenant, WorkflowRunKind.TestRun));
        await ((InMemoryActivityExecutionInspectionStore)shell.GetRequiredService<IActivityExecutionInspectionStore>()).SaveAsync(
            new ActivityExecutionInspectionProjection("legacy-activity", "test", "node", "node", "Sequence", "1",
                ActivityExecutionStatus.Completed, null, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                "first", "last", DateTimeOffset.UnixEpoch, ActivitySchedulingProvenance.Empty, [], [], [], [],
                new Dictionary<string, string>()));
        using var scope = shell.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceDetailsService>();
        var preserved = await service.GetAsync(new("test"), CancellationToken.None);
        Assert.NotNull(preserved);
        Assert.Equal(("test", "TestRun"), (preserved.Instance.WorkflowExecutionId, preserved.Instance.RunKind));
        Assert.False(preserved.ActivitiesDeferred);
        var browse = scope.ServiceProvider.GetRequiredService<IActivityExecutionBrowseSource>();
        var point = scope.ServiceProvider.GetRequiredService<IActivityExecutionInspectionService>();
        var test = (await browse.FindWorkflowAsync("test", CancellationToken.None))!;
        var activity = Assert.Single((await test.BrowseAsync(new("test", 10), null, CancellationToken.None))!.Page.Items);
        Assert.Equal("legacy-activity", activity.Execution.ActivityExecutionId);
        Assert.Equal("last", (await point.GetAsync(new("test", "legacy-activity"), CancellationToken.None))!.LastCheckpointId);
        Assert.True(preserved.Permissions!.CanManage);
        foreach (var id in new[] { "old-published", "other-tenant", "schedule" })
        {
            Assert.Null(await service.GetAsync(new(id), CancellationToken.None));
            Assert.Null(await browse.FindWorkflowAsync(id, CancellationToken.None));
            Assert.Null(await point.GetAsync(new(id, "legacy-activity"), CancellationToken.None));
        }
        Assert.Empty(reads.Tails);
        Assert.Equal(["test"], management.Checks);

        var authorization = new InspectionAccess(structure: false, values: true);
        using var hidden = Shell(new Reads(Views[0]), HostTenant, authorization);
        var hiddenStore = hidden.GetRequiredService<IWorkflowExecutionStateStore>();
        await hiddenStore.SaveAsync(Legacy("run-1", Tenant, WorkflowRunKind.TestRun));
        await hiddenStore.SaveAsync(Legacy("hidden-test", Tenant, WorkflowRunKind.TestRun));
        using var hiddenScope = hidden.CreateScope();
        var hiddenService = hiddenScope.ServiceProvider.GetRequiredService<IWorkflowInstanceDetailsService>();
        Assert.Null(await hiddenService.GetAsync(new("run-1"), CancellationToken.None));
        Assert.Null(await hiddenService.GetAsync(new("hidden-test"), CancellationToken.None));
        Assert.Equal(["run-1", "hidden-test"], authorization.StructureChecks);
        Assert.Empty(authorization.ValueChecks);
    }

    private static WorkflowExecutionState Legacy(string id, string tenant, WorkflowRunKind kind) =>
        new(id, new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Completed, null, DateTimeOffset.UnixEpoch, null, null, null, null, null, tenant,
            new Dictionary<string, string>()) { RunKind = kind };

    [Fact]
    public async Task The_list_shows_the_kernels_runs_with_their_status_times_and_workflow()
    {
        using var shell = Shell(new Reads(Views), HostTenant);

        var page = await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25));

        Assert.Equal(["run-5", "run-4", "run-3", "run-2", "run-1"], page.Items.Select(item => item.WorkflowExecutionId));
        var byId = page.Items.ToDictionary(item => item.WorkflowExecutionId);
        Assert.Equal(("Completed", "orders", "orders@1"), (byId["run-1"].Status, byId["run-1"].DefinitionId, byId["run-1"].ArtifactId));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_000), byId["run-1"].CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_500), byId["run-1"].CompletedAt);
        Assert.Equal("Running", byId["run-2"].Status);
        Assert.Null(byId["run-2"].CompletedAt);
        Assert.Equal("Suspended", byId["run-3"].Status);
        Assert.Equal(("Faulted", "native.burst.limit"), (byId["run-4"].Status, byId["run-4"].SubStatus));
        Assert.Equal("Cancelled", byId["run-5"].Status);
        Assert.Equal((5, 5), (page.Count, page.TotalCount));
    }

    [Fact]
    public async Task Definition_status_and_dates_filter_and_the_cursor_pages_as_the_runtime_list_does()
    {
        var reads = new Reads(Views);
        using var shell = Shell(reads, HostTenant);

        var orders = await ListAsync(shell, new ListWorkflowInstances(null, "orders", null, 25));
        // The definition filter reaches the foundation's read.
        Assert.Equal("orders", reads.Queries[^1].Labels!["definition"]);
        Assert.Equal(["run-3", "run-2", "run-1"], orders.Items.Select(item => item.WorkflowExecutionId));

        var completed = await ListAsync(shell, new ListWorkflowInstances("Completed", null, null, 25));
        Assert.Equal(["run-1"], completed.Items.Select(item => item.WorkflowExecutionId));

        var window = await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25,
            From: DateTimeOffset.FromUnixTimeMilliseconds(2_000), To: DateTimeOffset.FromUnixTimeMilliseconds(4_000)));
        // The runtime list's own window: from inclusive, to exclusive, on the run's start.
        Assert.Equal(["run-3", "run-2"], window.Items.Select(item => item.WorkflowExecutionId));

        var first = await ListAsync(shell, new ListWorkflowInstances(null, null, null, 2));
        Assert.True(first.HasNext);
        var second = await ListAsync(shell, new ListWorkflowInstances(null, null, null, 2, first.NextCursor));
        Assert.Equal(["run-5", "run-4", "run-3", "run-2"], first.Items.Concat(second.Items).Select(item => item.WorkflowExecutionId));

        var unknown = await ListAsync(shell, new ListWorkflowInstances("NotAStatus", null, null, 25));
        Assert.Empty(unknown.Items);

        // The rest of what Studio sends: an artifact, an execution id, and a run kind (every kernel run is a published run).
        Assert.Equal(["run-5", "run-4"], (await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25, ArtifactId: "invoices@2")))
            .Items.Select(item => item.WorkflowExecutionId));
        Assert.Equal(["run-2"], (await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25, WorkflowExecutionId: "run-2")))
            .Items.Select(item => item.WorkflowExecutionId));
        Assert.Equal(5, (await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25, RunKind: "PublishedRun"))).Items.Count);
        Assert.Empty((await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25, RunKind: "TestRun"))).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => ListAsync(shell, new ListWorkflowInstances(null, null, null, 25, RunKind: "Nonsense")));
    }

    [Fact]
    public async Task A_caller_of_another_tenant_or_across_tenants_sees_no_kernel_run()
    {
        using var other = Shell(new Reads(Views), PersistenceAccessContext.Scoped(new PersistenceScope("acme")));
        using var across = Shell(new Reads(Views), PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("kernel-run-list-test")));

        Assert.Empty((await ListAsync(other, new ListWorkflowInstances(null, null, null, 25))).Items);
        Assert.Empty((await ListAsync(across, new ListWorkflowInstances(null, null, null, 25))).Items);
    }

    [Fact]
    public async Task A_published_artifact_pins_its_executable_identity()
    {
        var reads = new Reads(Run("run-9", ActorStatus.Finished, "orders@1", "orders", 9_000, 9_100));
        using var shell = Shell(reads, HostTenant);
        var executables = (InMemoryWorkflowExecutableStore)shell.GetRequiredService<IWorkflowExecutableStore>();
        var executable = Compiled.ExecutableOf("orders@1", "orders", Compiled.Sequence("root", Compiled.WriteLine("w", Compiled.Literal("text", "hi"))));
        await executables.SaveAsync(executable);

        var item = Assert.Single((await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25))).Items);

        Assert.Equal((executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion, executable.Identity.ArtifactHash),
            (item.DefinitionVersionId, item.ArtifactVersion, item.ArtifactHash));
    }

    [Fact]
    public async Task A_run_without_a_kept_start_has_a_stable_unknown_start_and_an_unheld_artifact_no_version()
    {
        using var shell = Shell(new Reads(Run("run-old", ActorStatus.Running, "gone@3", "gone", startedAt: null)), HostTenant);

        var first = Assert.Single((await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25))).Items);
        var again = Assert.Single((await ListAsync(shell, new ListWorkflowInstances(null, null, null, 25))).Items);

        Assert.Equal((DateTimeOffset.UnixEpoch, (DateTimeOffset?)null), (first.CreatedAt, first.StartedAt));
        Assert.Equal(first.CreatedAt, again.CreatedAt);
        Assert.Equal(("gone@3", "gone", "", "", ""), (first.ArtifactId, first.DefinitionId, first.DefinitionVersionId, first.ArtifactVersion, first.ArtifactHash));
    }

    [Fact]
    public void The_feature_needs_the_hosts_kernel_runs_and_refuses_other_runtime_read_implementations()
    {
        var bare = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(bare));

        var custom = new ServiceCollection();
        custom.AddSingleton(new KernelRuns(Scope, _ => throw new NotSupportedException()));
        custom.AddScoped<IWorkflowInstanceListService, CustomList>();
        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(custom));
        Assert.Contains("registers another", refused.Message, StringComparison.Ordinal);

        custom.RemoveAll<IWorkflowInstanceListService>();
        custom.AddScoped<IWorkflowInstanceDetailsService, CustomDetails>();
        refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(custom));
        Assert.Contains(nameof(IWorkflowInstanceDetailsService), refused.Message, StringComparison.Ordinal);
    }

    private sealed class CustomList : IWorkflowInstanceListService
    {
        public Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CustomDetails : IWorkflowInstanceDetailsService
    {
        public Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InspectionAccess(bool structure, bool values) : IActivityInspectionContextAsync
    {
        public List<string> StructureChecks { get; } = [];
        public List<string> ValueChecks { get; } = [];
        public string TenantScope => Tenant;
        public string AuditSubject => "reader";
        public string RequestCorrelationId => "request";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("test");

        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
        {
            StructureChecks.Add(workflowExecution.WorkflowExecutionId);
            return ValueTask.FromResult(structure);
        }

        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
        {
            ValueChecks.Add(workflowExecution.WorkflowExecutionId);
            return ValueTask.FromResult(values);
        }

        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ManageAccess : IWorkflowRuntimeOperatorAccess
    {
        public List<string> Checks { get; } = [];

        public ValueTask<bool> CanManageAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
        {
            Checks.Add(workflowExecution.WorkflowExecutionId);
            return ValueTask.FromResult(true);
        }
    }
}
