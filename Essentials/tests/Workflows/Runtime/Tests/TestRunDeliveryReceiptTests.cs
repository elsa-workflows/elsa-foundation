using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TestRunDeliveryReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Receipt_BindsExactWaitAndConfiguration_AndSurvivesConsumedPayloadRemoval()
    {
        var payload = JsonSerializer.SerializeToElement(new { approved = true });
        var deliveryId = Guid.NewGuid().ToString("D");
        var receipt = new TestRunDeliveryReceipt("test-run", deliveryId, "wait-1", TestRunDeliveryReceipt.Digest(payload));
        var recorded = receipt.Record(Execution());
        Assert.True(receipt.MatchesExisting(recorded));
        Assert.Same(recorded, receipt.Record(recorded));
        var otherWait = new TestRunDeliveryReceipt("test-run", deliveryId, "wait-2", receipt.ConfigurationDigest);
        Assert.False(otherWait.MatchesExisting(recorded));
        Assert.Throws<InvalidOperationException>(() => otherWait.Record(recorded));
        Assert.Throws<InvalidOperationException>(() => receipt.Record(Execution() with { RunKind = WorkflowRunKind.PublishedRun }));
        Assert.Equal(deliveryId, new TestRunDeliveryReceipt("test-run", deliveryId.ToUpperInvariant(), "wait-1", receipt.ConfigurationDigest).DeliveryId);
    }

    [Fact]
    public void Receipt_LimitRefusesNewEventsWithoutEvictingRetryEvidence()
    {
        var execution = Execution();
        var digest = TestRunDeliveryReceipt.Digest(null);
        for (var index = 0; index < TestRunDeliveryReceipt.MaximumReceipts; index++)
            execution = new TestRunDeliveryReceipt("test-run", Guid.NewGuid().ToString("D"), "wait", digest).Record(execution);
        var overflow = new TestRunDeliveryReceipt("test-run", Guid.NewGuid().ToString("D"), "wait", digest);
        Assert.Throws<InvalidOperationException>(() => overflow.Record(execution));
        Assert.Equal(TestRunDeliveryReceipt.MaximumReceipts, execution.SystemMetadata.Count);
    }

    private static WorkflowExecutionState Execution() => new("execution",
        new("artifact", "definition", "draft", "draft", "hash"), WorkflowExecutionStatus.Suspended, null,
        Now, Now, Now, null, null, null, null, new Dictionary<string, string>())
    {
        RunKind = WorkflowRunKind.TestRun,
        TestScope = new("test-run", Now.AddMinutes(30), null, new("default"))
    };
}
