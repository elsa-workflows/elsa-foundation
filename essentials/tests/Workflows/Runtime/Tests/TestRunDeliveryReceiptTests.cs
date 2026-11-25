using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TestRunDeliveryReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Delivery_digest_pins_canonical_data_and_keeps_the_existing_absent_null_contract()
    {
        // Independently hashed canonical ASCII bytes: {"items":[1e0,2e0],"n":1e0,"text":"a"}.
        const string expected = "b2eaa6f53648c748a7f98db8fcf64a116cebbc27bcabeef7435024530261493f";
        var first = Parse("""{"items":[1,2],"n":1,"text":"a"}""");
        var equivalent = Parse("""{ "text":"\u0061", "n":1e0, "items":[1.0,2e0] }""");
        var actual = TestRunDeliveryReceipt.Digest(first);
        Assert.True(expected == actual, $"Delivery JSON hash golden: Actual={actual}. From Elsa 4's first stable release, this hash is frozen and changes require an explicit decision.");
        Assert.Equal(actual, TestRunDeliveryReceipt.Digest(equivalent));
        Assert.NotEqual(actual, TestRunDeliveryReceipt.Digest(Parse("""{"items":[2,1],"n":1,"text":"a"}""")));
        Assert.NotEqual(actual, TestRunDeliveryReceipt.Digest(Parse("""{"items":[1,2],"n":2,"text":"a"}""")));
        Assert.NotEqual(TestRunDeliveryReceipt.Digest(Parse("{}")), TestRunDeliveryReceipt.Digest(Parse("{\"n\":null}")));
        Assert.Equal(TestRunDeliveryReceipt.Digest(null), TestRunDeliveryReceipt.Digest(Parse("null")));
        var receipt = new TestRunDeliveryReceipt("test-run", "b66df011-c1a1-481c-ac79-d7dcbf1a5c55", "wait", actual);
        var recorded = receipt.Record(Execution());
        Assert.True(new TestRunDeliveryReceipt(receipt.TestRunId, receipt.DeliveryId, receipt.WaitId, TestRunDeliveryReceipt.Digest(equivalent)).MatchesExisting(recorded));
    }

    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

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
