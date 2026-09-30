using Elsa.Activities.Runtime.Core.Models;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, FR-008 and FR-009: each shell whose distributed runtime is active publishes one runnability entry, derived
/// from exactly the registries its requirement checker reads, so a placement query over the entry and the checker agree
/// on every requirement. It is published when the runtime activates, republished only when it changes, and withdrawn
/// when the shell stops.
/// </summary>
public sealed class RunnabilityReportTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();

    public static TheoryData<string, string> Pairs => new()
    {
        { "upgraded", "needs-v2" }, { "upgraded", "needs-v1" }, { "upgraded", "needs-blob" }, { "upgraded", "needs-approve" },
        { "old", "needs-v2" }, { "old", "needs-v1" }, { "old", "needs-blob" }, { "old", "needs-approve" },
        { "clr", "needs-approve" }, { "clr", "needs-v1" }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task A_members_published_entry_and_its_requirement_checker_agree(string registries, string work)
    {
        var member = await _cluster.StartAsync("host-a", registries switch
        {
            "upgraded" => setup => { Activates("1", "2")(setup); setup.StorageDrivers.Add("acme.blob"); },
            "old" => Activates("1"),
            _ => setup =>
            {
                setup.Consumers.Add(new RuntimeActivityConsumerCapability(WellKnownRuntimeActivityConsumers.ClrActivity, [RuntimeActivityDescriptor.InitialSchemaVersion]));
                setup.TypeRegistry = ApproveActivity.Registry();
            }
        });
        var executable = work switch
        {
            "needs-v2" => RuntimeWork.Needing("artifact-v2", ApprovalsConsumer, "2"),
            "needs-v1" => RuntimeWork.Needing("artifact-v1", ApprovalsConsumer, "1"),
            "needs-blob" => RuntimeWork.Needing("artifact-blob", ApprovalsConsumer, "1", "acme.blob"),
            _ => RuntimeWork.NeedingActivityType("artifact-approve", ApproveActivity.Alias)
        };

        await using var scope = member.Runtime.Services.CreateAsyncScope();
        var check = scope.ServiceProvider.GetRequiredService<IRuntimeRequirementChecker>().Check(RuntimeRequirementCheckSubject.FromExecutable(executable));
        var view = await member.Member.ReadFleetAsync(FleetReadMode.Fresh);
        var answer = MemberQuery.Placement([.. ExecutionPlacementRequirement.ToMemberRequirements(check)]).Evaluate(view);

        Assert.Equal(check.IsSatisfied, answer.Matches.Any(match => match.Identity == member.Member.Identity));
    }

    [Fact]
    public async Task The_entry_is_published_when_the_runtime_activates_republished_only_when_it_changes_and_withdrawn_when_it_stops()
    {
        var approvals = new MutableConsumer(ApprovalsConsumer, "1");
        var member = await _cluster.StartAsync("host-a", setup => setup.Consumers.Add(approvals));

        var activated = await PublishedAsync(member);
        Assert.Equal(["1"], Assert.Single(Assert.Single(activated.Report.Runnability!.Entries).Consumers).SchemaVersions);

        await member.Runtime.Pump.SweepOnceAsync();
        Assert.Equal(activated.ReportRevision, (await PublishedAsync(member)).ReportRevision);

        approvals.SupportedSchemaVersions = ["1", "2"];
        await member.Runtime.Pump.SweepOnceAsync();
        var changed = await PublishedAsync(member);
        Assert.True(changed.ReportRevision > activated.ReportRevision);
        Assert.Equal(["1", "2"], Assert.Single(Assert.Single(changed.Report.Runnability!.Entries).Consumers).SchemaVersions);

        await member.Runtime.StopAsync();
        Assert.Empty((await PublishedAsync(member)).Report.Runnability!.Entries);
    }

    /// <summary>
    /// Membership is per host and registries are per shell: the host composes one registry, every shell container built
    /// from copies of the host's registrations shares it, and the host's one source reports an entry per shell.
    /// </summary>
    [Fact]
    public async Task One_host_level_report_carries_an_entry_for_every_shell_built_from_the_host()
    {
        var host = new ServiceCollection();
        host.AddWorkflowRuntimeRunnabilityReport();

        await using var first = Shell(host);
        await using var second = Shell(host);
        var firstRegistry = first.GetRequiredService<ShellRunnabilityRegistry>();
        Assert.Same(firstRegistry, second.GetRequiredService<ShellRunnabilityRegistry>());

        firstRegistry.Record(first.GetRequiredService<DistributedRuntimeShell>(), new RunnabilityEntry([new RunnableConsumer(ApprovalsConsumer, ["1"])], [], []));
        second.GetRequiredService<ShellRunnabilityRegistry>().Record(second.GetRequiredService<DistributedRuntimeShell>(), new RunnabilityEntry([new RunnableConsumer(ApprovalsConsumer, ["1", "2"])], [], []));

        var section = await first.GetRequiredService<IMemberReportSource<RunnabilitySection>>().ReadAsync();
        Assert.Equal(2, section.Entries.Count);
        Assert.Equal(section, await second.GetRequiredService<IMemberReportSource<RunnabilitySection>>().ReadAsync());
    }

    /// <summary>
    /// A shell whose host composed no report still publishes its own entry, through the in-process membership of its own
    /// container, when the host composes the in-process default that every shell container is built with a copy of: the member
    /// is built from the report sources of the container that asks, so it is per shell, and a reload's new container gets a
    /// fresh one that carries nothing of the generation before it.
    /// </summary>
    [Fact]
    public async Task A_shell_on_a_host_without_the_report_publishes_its_own_entry_and_a_reload_yields_a_fresh_member()
    {
        var host = new ServiceCollection();
        host.TryAddInProcessClusterMembership();

        await using var first = Shell(host);
        var member = first.GetRequiredService<IClusterMembership>();
        first.GetRequiredService<ShellRunnabilityRegistry>().Record(first.GetRequiredService<DistributedRuntimeShell>(), new RunnabilityEntry([new RunnableConsumer(ApprovalsConsumer, ["1"])], [], []));

        var published = Assert.Single((await member.PublishReportAsync()).Report.Runnability!.Entries);
        Assert.Equal(["1"], Assert.Single(published.Consumers).SchemaVersions);

        await using var reloaded = Shell(host);
        var freshMember = reloaded.GetRequiredService<IClusterMembership>();
        Assert.NotSame(member, freshMember);
        Assert.Empty((await freshMember.PublishReportAsync()).Report.Runnability!.Entries);
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private static async Task<FleetMember> PublishedAsync(PlacementMember member) =>
        (await member.Member.ReadFleetAsync(FleetReadMode.Fresh)).Find(member.Member.Identity)!;

    private static ServiceProvider Shell(IServiceCollection host)
    {
        IServiceCollection shell = new ServiceCollection();
        foreach (var descriptor in host)
            shell.Add(descriptor);
        shell.AddLogging();
        shell.AddSingleton(TimeProvider.System);
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(shell);
        return shell.BuildServiceProvider();
    }
}
