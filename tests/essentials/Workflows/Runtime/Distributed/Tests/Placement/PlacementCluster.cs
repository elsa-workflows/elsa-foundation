using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Members of a <see cref="TestFleet"/> each running the distributed runtime, composed through the feature, over one
/// shared runtime state and one clock. A member's placement pump is driven by hand.
/// </summary>
internal sealed class PlacementCluster : IAsyncDisposable
{
    public const string ApprovalsConsumer = "acme.approvals";
    public const string ExecutionId = "execution-1";

    private readonly List<DistributedRuntimeNode> _nodes = [];

    public PlacementCluster()
    {
        Fleet = new TestFleet(Clock);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

    public TestFleet Fleet { get; }

    public SharedRuntimeState State { get; } = new();

    public DateTimeOffset Now => Clock.GetUtcNow();

    public IReadOnlyList<DistributedRuntimeNode> Nodes => _nodes;

    /// <summary>Joins <paramref name="hostId"/> and starts its runtime in a new process; its first sweep runs the join
    /// sweep unless <paramref name="sweep"/> is <see langword="false"/>.</summary>
    public async Task<PlacementMember> StartAsync(string hostId, Action<DistributedRuntimeNodeSetup>? configure = null, bool sweep = true)
    {
        var member = Fleet.Join(hostId);
        var node = DistributedRuntimeNode.Create(State, member, Clock, configure);
        member.ReportFrom(node.Services.GetRequiredService<IMemberReportSource<RunnabilitySection>>());
        _nodes.Add(node);
        if (sweep)
            await node.Pump.SweepOnceAsync();
        return new PlacementMember(member, node);
    }

    /// <summary>A member whose runtime activates the approvals consumer at <paramref name="versions"/>.</summary>
    public static Action<DistributedRuntimeNodeSetup> Activates(params string[] versions) =>
        setup => setup.Consumers.Add(new RuntimeActivityConsumerCapability(ApprovalsConsumer, versions));

    public async ValueTask DisposeAsync()
    {
        foreach (var node in _nodes)
            await node.DisposeAsync();
    }
}

/// <summary>A fleet member and its distributed runtime.</summary>
internal sealed record PlacementMember(TestMember Member, DistributedRuntimeNode Runtime)
{
    public string HostId => Member.Identity.HostId;
}

/// <summary>A runtime consumer whose supported schema versions a test changes while the member runs.</summary>
internal sealed class MutableConsumer(string consumerKey, params string[] versions) : IRuntimeActivityConsumerCapability
{
    public string ConsumerKey { get; } = consumerKey;

    public IReadOnlyCollection<string> SupportedSchemaVersions { get; set; } = versions;
}

/// <summary>A CLR activity type registered under the alias a newly installed module contributes.</summary>
internal sealed class ApproveActivity
{
    public const string Alias = "Acme.Approve";

    public static WellKnownTypeRegistry Registry()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(ApproveActivity), Alias);
        return registry;
    }
}
