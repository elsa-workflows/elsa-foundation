using System.Text.Json.Nodes;
using static Elsa.Workbench.Tests.NativeHostingScenario;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>Publishes through REST and kills the real Workbench while two ordinary background bodies are live.</summary>
/// <remarks>
/// Lease reclaim is wall-clock plus a live child poller. Sibling Workbench hosts on the same machine starve
/// that poller the same way parallel HTTP TestServers starved <c>DurableDrainCycleTests</c>, so this class
/// runs in its own non-parallel collection. After the kill, the test waits until the authored lease must
/// have expired and only then polls for a new process line, so a late reclaim is "expired but not claimed"
/// rather than a 90-second blur of lease time plus starvation.
/// </remarks>
[Collection(Name)]
public sealed class WorkbenchBackgroundActivityRecoveryTests
{
    public const string Name = nameof(WorkbenchBackgroundActivityRecoveryTests);

    [Theory]
    [InlineData(NativeHostingProfile.InProcess, "left", "right")]
    [InlineData(NativeHostingProfile.InProcess, "right", "left")]
    [InlineData(NativeHostingProfile.Clustered, "left", "right")]
    [InlineData(NativeHostingProfile.Clustered, "right", "left")]
    public async Task Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation(
        NativeHostingProfile profile, string first, string second)
    {
        await using var fixture = NativeHostingFixture.Create(profile);
        var original = await fixture.StartAsync("original");
        await LoginAsync(original);
        var journal = fixture.JournalDirectory;
        var executionId = await PublishAndStartAsync(original, journal);
        await WaitForStateAsync(original, executionId, state => ActivityStatus(state, "left") == "Running" && ActivityStatus(state, "right") == "Running");
        Assert.Equal(0, StartedCount(journal, "left"));
        Assert.Equal(0, StartedCount(journal, "right"));

        // Admission is durable even when this process never runs a worker. Enable workers only after it dies.
        var admitted = await fixture.RestartAsync(original, EnableWorkers);
        await WaitForAsync(admitted, () => StartedCount(journal, "left") >= 1 && StartedCount(journal, "right") >= 1,
            "both bodies to enter before either is released");
        Assert.Equal(0, StartedCount(journal, "final"));
        var initialLeft = StartedLines(journal, "left")[0];
        var initialRight = StartedLines(journal, "right")[0];

        var killedAt = DateTimeOffset.UtcNow;
        var restarted = await fixture.RestartAsync(admitted);
        Assert.Equal(original.ContentRoot, restarted.ContentRoot);
        await LoginAsync(restarted);
        await WaitUntilWorkerLeasesMustHaveExpiredAsync(killedAt);
        await WaitForAsync(restarted, () => StartedCount(journal, "left") >= 2 && StartedCount(journal, "right") >= 2,
            "a new process to reclaim both expired worker leases", () => DescribeJournal(journal));
        AssertRestartedWithSameLogicalIdentity(initialLeft, StartedLines(journal, "left")[1]);
        AssertRestartedWithSameLogicalIdentity(initialRight, StartedLines(journal, "right")[1]);

        // Persist the first receipt while its notification and repair sweep are held, then lose the process.
        File.WriteAllText(Path.Combine(journal, "hold-receipts"), "hold");
        File.WriteAllText(Path.Combine(journal, first + ".release"), "release");
        await WaitForAsync(restarted, () => File.Exists(Path.Combine(journal, first + ".receipt")),
            "the real EF receipt to commit before notification", () => DescribeJournal(journal));
        Assert.Equal(0, StartedCount(journal, "final"));
        var completedBodyCount = StartedCount(journal, first);
        var receiptRecovered = await fixture.RestartAsync(restarted, _ => File.Delete(Path.Combine(journal, "hold-receipts")));
        await LoginAsync(receiptRecovered);
        await WaitForStateAsync(receiptRecovered, executionId, instance => ActivityStatus(instance, first) == "Completed");
        Assert.Equal(completedBodyCount, StartedCount(journal, first));
        Assert.Equal(0, StartedCount(journal, "final"));
        File.WriteAllText(Path.Combine(journal, second + ".release"), "release");
        var completed = await WaitForStateAsync(receiptRecovered, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Empty(completed["incidents"]!.AsArray());
        Assert.Equal(1, StartedCount(journal, "final"));
        Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "final");
        Assert.Equal("Completed", ActivityStatus(completed, "left"));
        Assert.Equal("Completed", ActivityStatus(completed, "right"));

        // Accepted native history survives another process loss without a second logical final execution.
        var terminal = await fixture.RestartAsync(receiptRecovered);
        await LoginAsync(terminal);
        await WaitForStateAsync(terminal, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Equal(1, StartedCount(journal, "final"));
        Assert.Equal(completedBodyCount, StartedCount(journal, first));
    }

    [Theory]
    [InlineData(NativeHostingProfile.InProcess)]
    [InlineData(NativeHostingProfile.Clustered)]
    public async Task Explicit_cancellation_stops_held_bodies_and_survives_process_restart(NativeHostingProfile profile)
    {
        await using var fixture = NativeHostingFixture.Create(profile);
        var host = await fixture.StartAsync("cancellation", runWorkers: true);
        await LoginAsync(host);
        var journal = fixture.JournalDirectory;
        var executionId = await PublishAndStartAsync(host, journal);
        await WaitForAsync(host, () => StartedCount(journal, "left") == 1 && StartedCount(journal, "right") == 1,
            "both background bodies to enter before cancellation");
        await WaitForAsync(host, () => File.Exists(Path.Combine(journal, "left.claim.json")) && File.Exists(Path.Combine(journal, "right.claim.json")),
            "the real pre-cancellation worker claims to be captured");
        var originalProcessId = int.Parse(StartedLines(journal, "left")[0].Split('|')[0]);
        await PostAsync(host, "/runtime/workflows/alteration-plans", new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, Guid.NewGuid().ToString("N"));
        await WaitForStateAsync(host, executionId, state => state["instance"]?["status"]?.GetValue<string>() == "Cancelled");
        await WaitForAsync(host, () => File.Exists(Path.Combine(journal, "left.cancelled")) && File.Exists(Path.Combine(journal, "right.cancelled")),
            "both held bodies to observe committed business cancellation through claim loss");
        Assert.Equal(0, StartedCount(journal, "final"));

        File.WriteAllText(Path.Combine(journal, "left.release"), "release");
        File.WriteAllText(Path.Combine(journal, "right.release"), "release");
        var restarted = await fixture.RestartAsync(host, _ =>
            File.WriteAllText(Path.Combine(journal, "attempt-late-receipt-after-restart"), "submit"));
        await LoginAsync(restarted);
        await WaitForAsync(restarted, () => File.Exists(Path.Combine(journal, "left.late-receipt.json")) &&
                                                  File.Exists(Path.Combine(journal, "right.late-receipt.json")),
            "the replacement process to reject both captured late receipts");
        foreach (var key in new[] { "left", "right" })
        {
            var capturedClaim = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, key + ".claim.json")))!;
            var lateReceipt = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, key + ".late-receipt.json")))!;
            Assert.NotEqual(originalProcessId, lateReceipt["ProcessId"]!.GetValue<int>());
            Assert.Equal(executionId, lateReceipt["WorkflowExecutionId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["WorkId"]!.GetValue<string>(), lateReceipt["WorkId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["ClaimOwnerId"]!.GetValue<string>(), lateReceipt["ClaimOwnerId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["ClaimFence"]!.GetValue<long>(), lateReceipt["ClaimFence"]!.GetValue<long>());
            Assert.Equal(capturedClaim["Revision"]!.GetValue<long>(), lateReceipt["Revision"]!.GetValue<long>());
            Assert.Contains("cancellation tombstone fenced this receipt", lateReceipt["Message"]!.GetValue<string>());
            Assert.Equal("Cancelled", lateReceipt["Status"]!.GetValue<string>());
            Assert.False(lateReceipt["HasResult"]!.GetValue<bool>());
        }

        var state = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Cancelled");
        Assert.Equal("Cancelled", ActivityStatus(state, "left"));
        Assert.Equal("Cancelled", ActivityStatus(state, "right"));
        Assert.Equal(1, StartedCount(journal, "left"));
        Assert.Equal(1, StartedCount(journal, "right"));
        Assert.Equal(0, StartedCount(journal, "final"));
    }

    [Fact]
    public void Remaining_lease_wait_is_zero_only_after_the_authored_lease_plus_one_poll()
    {
        var killedAt = new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(WorkerLeaseDuration + WorkerPollInterval - TimeSpan.FromSeconds(5), RemainingWaitAfterKill(killedAt, killedAt + TimeSpan.FromSeconds(5), WorkerLeaseDuration, WorkerPollInterval));
        Assert.Equal(WorkerPollInterval, RemainingWaitAfterKill(killedAt, killedAt + WorkerLeaseDuration, WorkerLeaseDuration, WorkerPollInterval));
        Assert.Equal(TimeSpan.Zero, RemainingWaitAfterKill(killedAt, killedAt + WorkerLeaseDuration + WorkerPollInterval, WorkerLeaseDuration, WorkerPollInterval));
        Assert.Equal(TimeSpan.Zero, RemainingWaitAfterKill(killedAt, killedAt + TimeSpan.FromSeconds(45), WorkerLeaseDuration, WorkerPollInterval));
    }
}

[CollectionDefinition(WorkbenchBackgroundActivityRecoveryTests.Name, DisableParallelization = true)]
public sealed class WorkbenchBackgroundActivityRecoveryTestCollection;
