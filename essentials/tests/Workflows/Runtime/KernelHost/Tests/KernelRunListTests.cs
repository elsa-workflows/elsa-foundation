using Elsa.ActorFoundation.Hosting;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Incidents;
using Kernel.Foundation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>A kernel shell's Runs list: the kernel's runs in the host's tenant, through the runtime API's own list.</summary>
public sealed class KernelRunListTests
{
    private const string Tenant = "default";
    private static readonly BoundScope Scope = EmbeddedTenant.Bind(Tenant);

    /// <summary>The foundation's reads over fixed views, two to a page, so a list reads every page.</summary>
    private sealed class Reads(params ActorView[] views) : IActorReads
    {
        public List<ActorQuery> Queries { get; } = [];

        public ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(views.FirstOrDefault(view => view.Header.Ref == actor));

        public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var matching = views.Where(view => view.Header.Type == query.Type &&
                (query.Labels is null || query.Labels.All(label => view.Header.Labels.GetValueOrDefault(label.Key) == label.Value))).ToArray();
            var skip = from is { } token ? int.Parse(token.Value) : 0;
            var page = matching.Skip(skip).Take(2).ToArray();
            return ValueTask.FromResult(new Page<ActorView>(page, skip + 2 < matching.Length ? new PageToken((skip + 2).ToString()) : null, 1_000));
        }

        public ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static ActorView Run(string id, ActorStatus status, string workflow, string definition, long? startedAt, long? endedAt = null,
        bool suspended = false, string? subStatus = null)
    {
        var typeData = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Model"] = "native", ["Workflow"] = workflow, ["Version"] = 14, ["Suspended"] = suspended, ["SubStatus"] = subStatus,
            ["Ephemeral"] = false, ["History"] = 0, ["Definition"] = definition == workflow ? null : definition, ["StartedAt"] = startedAt
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
    private static ServiceProvider Shell(Reads reads, PersistenceAccessContext access)
    {
        var services = new ServiceCollection();
        var executables = new InMemoryWorkflowExecutableStore();
        services.AddSingleton<IWorkflowExecutableStore>(executables);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(access));
        services.AddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.AddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.AddSingleton<IActivityInspectionContextAsync>(new AllowAllActivityExecutionInspectionAuthorizationContext());
        services.AddSingleton(new KernelRuns(Scope, _ => ValueTask.FromResult<IActorReads>(reads)));
        // The runtime API's default list.
        services.AddScoped<IWorkflowInstanceListService, WorkflowInstanceListService>();
        new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceListView> ListAsync(ServiceProvider shell, ListWorkflowInstances request) =>
        shell.CreateScope().ServiceProvider.GetRequiredService<IWorkflowInstanceListService>().ListAsync(request, CancellationToken.None);

    private static readonly PersistenceAccessContext HostTenant = PersistenceAccessContext.Scoped(new PersistenceScope(Tenant));

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
    public void The_feature_needs_the_hosts_kernel_runs_and_refuses_another_list()
    {
        var bare = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(bare));

        var custom = new ServiceCollection();
        custom.AddSingleton(new KernelRuns(Scope, _ => throw new NotSupportedException()));
        custom.AddScoped<IWorkflowInstanceListService, CustomList>();
        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(custom));
        Assert.Contains("registers another", refused.Message, StringComparison.Ordinal);
    }

    private sealed class CustomList : IWorkflowInstanceListService
    {
        public Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
