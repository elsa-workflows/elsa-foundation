using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivitySchedulingMetadataContextTests
{
    private const string MetadataKey = "test.route";

    [Fact]
    public void ChildOutcomeContext_SnapshotsCommittedSchedulingMetadata_AndLegacyConstructorUsesEmptyMetadata()
    {
        var source = new Dictionary<string, string> { [MetadataKey] = "committed" };
        var notification = new ActivityChildNotifiedContext("child-execution", "child-node", "test.notification");
        var context = new ActivityChildOutcomeContext(
            new ParentContextStub(),
            "child-execution",
            "child-node",
            null,
            new ActivityFault("test.fault", "fault"),
            "incident-1",
            [notification],
            source);
        var legacy = new ActivityChildOutcomeContext(
            new ParentContextStub(),
            "child-execution",
            "child-node",
            null,
            new ActivityFault("test.fault", "fault"),
            "incident-1",
            [notification]);

        source[MetadataKey] = "mutated";

        Assert.Equal("committed", context.ChildSchedulingMetadata[MetadataKey]);
        Assert.Empty(legacy.ChildSchedulingMetadata);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)context.ChildSchedulingMetadata)[MetadataKey] = "changed");
        Assert.Throws<ArgumentNullException>(() => new ActivityChildOutcomeContext(
            new ParentContextStub(), "child-execution", "child-node", null,
            new ActivityFault("test.fault", "fault"), "incident-1", [notification], null!));
    }

    [Fact]
    public void DescendantFaultContext_SnapshotsViaChildSchedulingMetadata_AndLegacyConstructorUsesEmptyMetadata()
    {
        var source = new Dictionary<string, string> { [MetadataKey] = "committed" };
        var context = new ActivityDescendantFaultedContext(
            new ParentContextStub(),
            "descendant-execution",
            "descendant-node",
            "via-child-execution",
            "via-child-node",
            "incident-1",
            null,
            "test.fault",
            null,
            source);
        var legacy = new ActivityDescendantFaultedContext(
            new ParentContextStub(),
            "descendant-execution",
            "descendant-node",
            "via-child-execution",
            "via-child-node");

        source[MetadataKey] = "mutated";

        Assert.Equal("committed", context.ViaChildSchedulingMetadata[MetadataKey]);
        Assert.Empty(legacy.ViaChildSchedulingMetadata);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)context.ViaChildSchedulingMetadata)[MetadataKey] = "changed");
        Assert.Throws<ArgumentNullException>(() => new ActivityDescendantFaultedContext(
            new ParentContextStub(), "descendant-execution", "descendant-node", "via-child-execution", "via-child-node",
            null, null, null, null, null!));
    }

    private sealed class ParentContextStub : IActivityExecutionContext
    {
        public IActivity Activity => null!;
        public CancellationToken CancellationToken => System.Threading.CancellationToken.None;
    }
}
