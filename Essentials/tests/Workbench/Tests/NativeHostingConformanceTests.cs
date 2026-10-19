using System.Text.Json.Nodes;
using static Elsa.Workbench.Tests.NativeHostingScenario;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workbench.Tests;

/// <summary>Exercises native routing and continuation through two real processes and shared EF stores.</summary>
[Collection(WorkbenchBackgroundActivityRecoveryTests.Name)]
public sealed class NativeHostingConformanceTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData("left", "right")]
    [InlineData("right", "left")]
    public async Task Departed_routing_owner_is_reclaimed_and_its_real_transport_command_completes_on_survivor(
        string first, string second)
    {
        Skip.If(OperatingSystem.IsWindows(), "The fixture suspends its owned coordinator with POSIX signals.");
        await using var fixture = NativeHostingFixture.Create(NativeHostingProfile.Clustered, observeNativeHosting: true);
        fixture.HoldWorkerClaims("owner");
        var hosts = await fixture.SerialStartAsync(("owner", true), ("survivor", true));
        var owner = hosts[0];
        var survivor = hosts[1];
        var ownerId = $"{fixture.ScenarioId}-owner";
        var survivorId = $"{fixture.ScenarioId}-survivor";
        var journal = fixture.JournalDirectory;
        Assert.NotEqual(owner.ProcessId, survivor.ProcessId);
        Assert.False(owner.HasExited);
        Assert.False(survivor.HasExited);
        AssertPinnedPackage(fixture, owner);
        AssertPinnedPackage(fixture, survivor);
        await LoginAsync(owner);
        await LoginAsync(survivor);

        var executionId = await PublishAndStartAsync(owner, journal);
        await WaitForStateAsync(owner, executionId,
            state => ActivityStatus(state, first) == "Running" && ActivityStatus(state, second) == "Running");
        await WaitForAsync(survivor, () => StartedCount(journal, first) == 1 && StartedCount(journal, second) == 1,
            "both real background bodies to start on the survivor", () => DescribeJournal(journal));
        var firstStart = Assert.Single(StartedLines(journal, first));
        var secondStart = Assert.Single(StartedLines(journal, second));
        Assert.Equal(survivor.ProcessId.ToString(), firstStart.Split('|')[0]);
        Assert.Equal(survivor.ProcessId.ToString(), secondStart.Split('|')[0]);

        var originalFence = await WaitForEventAsync(owner, journal, "ownership-acquired", executionId,
            record => Value<int>(record, "ProcessId") == owner.ProcessId && Value<string>(record, "OwnerId") == ownerId);
        var composition = await WaitForEventAsync(owner, journal, "composition", executionId: null,
            record => Value<int>(record, "ProcessId") == owner.ProcessId &&
                      IsLive(record, "FreshMembershipMembers", ownerId) && IsLive(record, "FreshMembershipMembers", survivorId));
        AssertEffectiveClusterComposition(fixture, composition);
        Assert.Equal(ownerId, Value<string>(composition, "LocalHostId"));
        Assert.Equal(0, StartedCount(journal, "final"));

        // At this quiescent wait the native drain has released its execution fence. Routing placement remains.
        // Freeze the owner so its resumption pump cannot consume the survivor's receipt before it is forwarded.
        await owner.SuspendAsync();
        File.WriteAllText(Path.Combine(journal, "hold-receipts"), "hold");
        File.WriteAllText(Path.Combine(journal, first + ".release"), "release");
        var bodyClaim = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, first + ".claim.json")))!.AsObject();
        JsonObject? receipt = null;
        await WaitForAsync(survivor, () =>
        {
            receipt = Directory.EnumerateFiles(journal, "work-*.receipt.json")
                .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject())
                .FirstOrDefault(record => Value<string>(record, "Stage") == "committed" &&
                    Value<string>(record, "WorkId") == Value<string>(bodyClaim, "WorkId") &&
                    Value<int>(record, "ProcessId") == survivor.ProcessId);
            return receipt is not null;
        },
            "the survivor's real EF receipt commit", () => DescribeJournal(journal));
        Assert.True(File.Exists(Path.Combine(journal, first + ".receipt")));
        var receiptCommittedAt = Value<DateTimeOffset>(receipt!, "RecordedAt");
        File.Delete(Path.Combine(journal, "hold-receipts"));

        // This command is created by real resumption after the worker enqueues its persisted coordinator work.
        // Seeing a body finish or enabling a transport feature alone does not establish that transport was used.
        var sent = await WaitForEventAsync(survivor, journal, "transport-sent", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                      Value<string>(record, "CommandKind") == "RunSchedulerWork" &&
                      record["CommandSource"]?.GetValue<string>() == "runtime-resumption" &&
                      Value<DateTimeOffset>(record, "CommandEnqueuedAt") >= receiptCommittedAt &&
                      record["PlacementOwnerId"]?.GetValue<string>() == ownerId);
        var transportId = Value<string>(sent, "TransportItemId");
        var placementToken = Value<long>(sent, "PlacementToken");
        var placementExpiry = Value<DateTimeOffset>(sent, "PlacementExpiresAt");
        Assert.NotEmpty(transportId);
        Assert.Equal(survivorId, Value<string>(sent, "LocalHostId"));
        Assert.Null(sent["PlacementBeforeError"]);
        Assert.Null(sent["PlacementAfterError"]);
        Assert.Equal(ownerId, Value<string>(sent, "PlacementAfterOwnerId"));
        Assert.Equal(placementToken, Value<long>(sent, "PlacementAfterToken"));
        foreach (var phase in new[] { "Before", "After" })
        {
            Assert.Null(sent[$"Membership{phase}Error"]);
            Assert.Equal("Fresh", Value<string>(sent, $"Membership{phase}ReadMode"));
            Assert.True(IsLive(sent, $"Membership{phase}Members", ownerId));
            Assert.True(IsLive(sent, $"Membership{phase}Members", survivorId));
            Assert.True(Value<DateTimeOffset>(sent, $"Membership{phase}JudgedAt") < placementExpiry);
        }
        Assert.True(Value<DateTimeOffset>(sent, "RecordedAt") < placementExpiry);
        Assert.Equal(0, StartedCount(journal, "final"));
        await owner.CrashAsync();
        Assert.True(owner.HasExited);

        // Observe an actual release before TTL expiry, followed by a fresh snapshot showing departure.
        // The snapshot is independent of the pump's decision; FailoverTests cover its fresh-read authorization.
        var released = await WaitForEventAsync(survivor, journal, "placement-released", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                      Value<string>(record, "PlacementOwnerId") == ownerId &&
                      Value<long>(record, "PlacementToken") == placementToken && Value<bool>(record, "ReleaseResult"));
        Assert.Equal(placementExpiry, Value<DateTimeOffset>(released, "PlacementExpiresAt"));
        Assert.Equal("Fresh", Value<string>(released, "FreshMembershipReadMode"));
        Assert.True(Value<DateTimeOffset>(released, "FreshMembershipJudgedAt") < placementExpiry);
        var departed = Member(released, "FreshMembershipMembers", ownerId);
        Assert.False(Value<bool>(departed, "IsLive"));
        Assert.True(IsLive(released, "FreshMembershipMembers", survivorId));

        await WaitForEventAsync(survivor, journal, "placement-claimed", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                      Value<string>(record, "PlacementOwnerId") == survivorId &&
                      Value<long>(record, "PlacementToken") > placementToken);
        var leased = await WaitForEventAsync(survivor, journal, "transport-leased", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                      Value<string>(record, "TransportItemId") == transportId && Value<string>(record, "LeaseOwnerId") == survivorId);
        await WaitForEventAsync(survivor, journal, "transport-acked", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                      Value<string>(record, "TransportItemId") == transportId && Value<string>(record, "LeaseOwnerId") == survivorId &&
                      Value<long>(record, "LeaseToken") == Value<long>(leased, "LeaseToken") && Value<bool>(record, "AckResult"));
        await WaitForStateAsync(survivor, executionId,
            state => ActivityStatus(state, first) == "Completed" && ActivityStatus(state, second) == "Running");
        var survivorComposition = await WaitForEventAsync(survivor, journal, "composition", executionId: null,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId);
        AssertEffectiveClusterComposition(fixture, survivorComposition);
        Assert.Equal(survivorId, Value<string>(survivorComposition, "LocalHostId"));
        Assert.Equal(0, StartedCount(journal, "final"));

        File.WriteAllText(Path.Combine(journal, second + ".release"), "release");
        var completed = await WaitForStateAsync(survivor, executionId,
            state => state["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Empty(completed["incidents"]!.AsArray());
        Assert.Equal("Completed", ActivityStatus(completed, first));
        Assert.Equal("Completed", ActivityStatus(completed, second));
        Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "final");
        Assert.Equal(survivor.ProcessId.ToString(), Assert.Single(StartedLines(journal, "final")).Split('|')[0]);
        Assert.Equal(firstStart, Assert.Single(StartedLines(journal, first)));
        Assert.Equal(secondStart, Assert.Single(StartedLines(journal, second)));
        await WaitForEventAsync(survivor, journal, "ownership-acquired", executionId,
            record => Value<int>(record, "ProcessId") == survivor.ProcessId && Value<string>(record, "OwnerId") == survivorId &&
                      Value<long>(record, "FencingToken") > Value<long>(originalFence, "FencingToken"));
        Assert.False(survivor.HasExited);
    }

    [SkippableFact]
    public async Task Original_uncancelled_checkpoint_is_rejected_after_a_real_successor_acquires()
    {
        Skip.If(OperatingSystem.IsWindows(), "The fixture suspends its owned coordinator with POSIX signals.");
        await using var fixture = NativeHostingFixture.Create(NativeHostingProfile.Clustered, observeStaleWrite: true);
        fixture.HoldWorkerClaims("owner");
        var hosts = await fixture.SerialStartAsync(("owner", true), ("survivor", true));
        var owner = hosts[0];
        var survivor = hosts[1];
        var journal = fixture.JournalDirectory;
        var ownerId = $"{fixture.ScenarioId}-owner";
        var survivorId = $"{fixture.ScenarioId}-survivor";
        Assert.NotEqual(owner.ProcessId, survivor.ProcessId);
        AssertPinnedPackage(fixture, owner);
        AssertPinnedPackage(fixture, survivor);
        await LoginAsync(owner);
        await LoginAsync(survivor);

        try
        {
            var executionId = await PublishAndStartAsync(owner, journal);
            await WaitForStateAsync(owner, executionId,
                state => ActivityStatus(state, "left") == "Running" && ActivityStatus(state, "right") == "Running");
            await WaitForAsync(survivor, () => StartedCount(journal, "left") == 1 && StartedCount(journal, "right") == 1,
                "both held bodies before the original checkpoint proof", () => DescribeJournal(journal));
            var leftStart = Assert.Single(StartedLines(journal, "left"));
            var rightStart = Assert.Single(StartedLines(journal, "right"));
            Assert.Equal(survivor.ProcessId.ToString(), leftStart.Split('|')[0]);
            Assert.Equal(survivor.ProcessId.ToString(), rightStart.Split('|')[0]);
            var selector = Path.Combine(journal, "stale.execution-id");
            File.WriteAllText(selector + ".tmp", executionId);
            File.Move(selector + ".tmp", selector);
            File.WriteAllText(Path.Combine(journal, "left.release"), "release");

            var captured = await WaitForEventAsync(owner, journal, "stale-commit-captured", executionId,
                record => Value<int>(record, "ProcessId") == owner.ProcessId, "native-stale.*.json");
            output.WriteLine(captured.ToJsonString());
            var original = await WaitForEventAsync(owner, journal, "stale-commit-public", executionId,
                record => Value<string>(record, "CommitId") == Value<string>(captured, "CommitId"), "native-stale.*.json");
            foreach (var field in new[] { "CommitId", "CheckpointId", "WorkItemId", "Fingerprint", "CommitObjectId", "CancellationTokenHash", "ExpectedFence" })
                Assert.True(JsonNode.DeepEquals(original[field], captured[field]), $"The original {field} changed before the EF boundary.");
            Assert.Equal("ActivityCompleted", Value<string>(captured, "CheckpointName"));
            Assert.Equal("BackgroundActivityResult", Value<string>(captured, "CheckpointReason"));
            Assert.Equal("Immediate", Value<string>(captured, "DecisionMode"));
            Assert.False(Value<bool>(captured, "CoalescedFlush"));
            Assert.True(Value<bool>(captured, "CancellationTokenCanBeCanceled"));
            Assert.False(Value<bool>(captured, "CancellationRequested"));
            Assert.Empty(captured["ConsumedSchedulerWorkItemIds"]!.AsArray());
            var oldFence = captured["ExpectedFence"]!.AsObject();
            Assert.Equal(ownerId, Value<string>(oldFence, "OwnerId"));
            var oldToken = Value<long>(oldFence, "FencingToken");
            Assert.Null(captured["Marker"]);
            Assert.Equal("ResultReady", Value<string>(captured, "BackgroundStatus"));
            Assert.Equal("Running", Value<string>(captured, "ActivityStatus"));
            var leftClaim = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, "left.claim.json")))!.AsObject();
            Assert.Equal(Value<string>(leftClaim, "WorkId"),
                Assert.Single(captured["ReceiptAcceptances"]!.AsArray())!["WorkId"]!.GetValue<string>());
            var scheduler = captured["SchedulerRow"]!.AsObject();
            Assert.Equal(Value<string>(captured, "WorkItemId"), Value<string>(scheduler, "WorkItemId"));
            Assert.Null(scheduler["ClaimOwnerId"]);
            Assert.Equal(0, Value<long>(scheduler, "ClaimToken"));
            Assert.Null(scheduler["ClaimedAtUtcTicks"]);
            Assert.Null(scheduler["VisibleAfterUtcTicks"]);

            var suspendedAt = DateTimeOffset.UtcNow;
            await owner.SuspendAsync();
            var successor = await WaitForEventAsync(survivor, journal, "stale-successor-acquired", executionId,
                record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                          Value<long>(record, "FencingToken") > oldToken, "native-stale.*.json");
            output.WriteLine(successor.ToJsonString());
            Assert.Equal(survivorId, Value<string>(successor, "OwnerId"));
            Assert.Equal(survivorId, Value<string>(successor, "PersistedOwnerId"));
            Assert.Equal(Value<string>(successor, "LeaseId"), Value<string>(successor, "PersistedLeaseId"));
            Assert.Equal(Value<long>(successor, "FencingToken"), Value<long>(successor, "PersistedFencingToken"));
            Assert.True(Value<bool>(successor, "PersistedLeaseUnexpired"));
            Assert.True(Value<DateTimeOffset>(successor, "LeaseExpiresAt") > DateTimeOffset.UtcNow);
            await owner.ResumeAsync();
            var heartbeatBlocked = await WaitForEventAsync(owner, journal, "stale-heartbeat-blocked", executionId,
                record => Value<int>(record, "ProcessId") == owner.ProcessId &&
                          Value<long>(record, "FencingToken") == oldToken &&
                          Value<string>(record, "LeaseId") == Value<string>(oldFence, "LeaseId"), "native-stale.*.json");
            output.WriteLine(heartbeatBlocked.ToJsonString());
            Assert.False(Value<bool>(heartbeatBlocked, "CancellationRequested"));
            // The normal scheduler dispatch ceiling is 30 minutes. This proof must retain its original live token.
            Assert.True(DateTimeOffset.UtcNow - suspendedAt < TimeSpan.FromMinutes(30));
            File.WriteAllText(Path.Combine(journal, "stale.commit.release"), "release");

            var delegating = await WaitForEventAsync(owner, journal, "stale-commit-delegating", executionId,
                record => Value<string>(record, "CommitId") == Value<string>(captured, "CommitId"), "native-stale.*.json");
            output.WriteLine(delegating.ToJsonString());
            var attempted = await WaitForEventAsync(owner, journal, "stale-commit-attempted", executionId,
                record => Value<string>(record, "CommitId") == Value<string>(captured, "CommitId"), "native-stale.*.json");
            output.WriteLine(attempted.ToJsonString());
            Assert.Equal("stale-token-rejected", Value<string>(attempted, "Outcome"));
            Assert.Equal("Elsa.Workflows.Runtime.Core.Exceptions.RuntimeStaleFencingTokenException", Value<string>(attempted, "ExceptionType"));
            Assert.Equal("StaleToken", Value<string>(attempted, "FencingRejectionReason"));
            Assert.Equal(oldToken, Value<long>(attempted, "PresentedFencingToken"));
            Assert.Equal(Value<long>(successor, "FencingToken"), Value<long>(attempted, "ExceptionCurrentFencingToken"));
            foreach (var record in new[] { delegating, attempted })
            {
                Assert.False(Value<bool>(record, "CancellationRequested"));
                Assert.Null(record["Marker"]);
                Assert.Equal(survivorId, Value<string>(record, "CurrentOwnerId"));
                Assert.Equal(Value<long>(successor, "FencingToken"), Value<long>(record, "PersistedFencingToken"));
                Assert.True(Value<DateTimeOffset>(record, "CurrentLeaseExpiresAt") > Value<DateTimeOffset>(record, "RecordedAt"));
                foreach (var field in new[] { "Fingerprint", "CommitObjectId", "CancellationTokenHash", "ExpectedFence", "BackgroundStatus", "ReceiptDigest", "BackgroundRevision", "ActivityStatus", "ActivityRevision", "ActivityContentHash", "SchedulerRow" })
                    Assert.True(JsonNode.DeepEquals(captured[field], record[field]), $"The original checkpoint changed protected {field} at {record["Stage"]}.");
            }
            Assert.Equal(0, StartedCount(journal, "final"));
            File.WriteAllText(Path.Combine(journal, "stale.heartbeat.release"), "release");
            JsonObject[] HeartbeatOutcomes() => ReadEvents(journal, "native-stale.*.json")
                .Where(record => record["ExecutionId"]?.GetValue<string>() == executionId &&
                                 record["FencingToken"]?.GetValue<long>() == oldToken &&
                                 record["Stage"]?.GetValue<string>() is "stale-heartbeat-delegated" or "stale-heartbeat-cancelled")
                .ToArray();
            await WaitForAsync(owner, () => HeartbeatOutcomes().Length != 0,
                "the real old-heartbeat result or cleanup cancellation", () => DescribeJournal(journal));
            var heartbeatOutcome = Assert.Single(HeartbeatOutcomes());
            output.WriteLine(heartbeatOutcome.ToJsonString());
            if (Value<string>(heartbeatOutcome, "Stage") == "stale-heartbeat-cancelled")
            {
                Assert.True(Value<bool>(heartbeatOutcome, "CancellationRequested"));
                Assert.False(Value<bool>(heartbeatOutcome, "Delegated"));
            }
            File.Delete(selector);
            File.WriteAllText(Path.Combine(journal, "stale.successor.release"), "release");
            await WaitForStateAsync(survivor, executionId,
                state => ActivityStatus(state, "left") == "Completed" && ActivityStatus(state, "right") == "Running");
            Assert.Equal(0, StartedCount(journal, "final"));
            File.WriteAllText(Path.Combine(journal, "right.release"), "release");
            var completed = await WaitForStateAsync(survivor, executionId,
                state => state["instance"]?["status"]?.GetValue<string>() == "Completed");
            output.WriteLine(completed.ToJsonString());
            Assert.Empty(completed["incidents"]!.AsArray());
            Assert.Equal(survivor.ProcessId.ToString(), Assert.Single(StartedLines(journal, "final")).Split('|')[0]);
            Assert.Equal(leftStart, Assert.Single(StartedLines(journal, "left")));
            Assert.Equal(rightStart, Assert.Single(StartedLines(journal, "right")));
            Assert.False(owner.HasExited);
            Assert.False(survivor.HasExited);
        }
        finally
        {
            File.Delete(Path.Combine(journal, "stale.execution-id"));
            foreach (var control in new[] { "stale.commit.release", "stale.heartbeat.release", "stale.successor.release" })
                File.WriteAllText(Path.Combine(journal, control), "release");
            if (!owner.HasExited)
                await owner.ResumeAsync();
        }
    }

    private static void AssertEffectiveClusterComposition(NativeHostingFixture fixture, JsonObject record)
    {
        Assert.Null(record["CompositionError"]);
        Assert.EndsWith(".DistributedWorkflowExecutionActorProvider", Value<string>(record, "ActorProviderType"));
        Assert.EndsWith(".EfClusterMembership", Value<string>(record, "MembershipProviderType"));
        Assert.EndsWith(".EfExecutionPlacementStore", Value<string>(record, "PlacementStoreBackendType"));
        Assert.EndsWith(".EfExecutionCommandTransport", Value<string>(record, "CommandTransportBackendType"));
        Assert.Null(record["FreshMembershipError"]);
        Assert.Equal("Fresh", Value<string>(record, "FreshMembershipReadMode"));
        foreach (var store in new[] { "Runtime", "Membership", "Placement", "CommandTransport" })
        {
            Assert.Null(record[$"{store}ObservationError"]);
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", Value<string>(record, $"{store}Provider"));
            Assert.Equal("Microsoft.Data.Sqlite.SqliteConnection", Value<string>(record, $"{store}DbConnectionType"));
            Assert.Equal(Value<string>(record, $"Configured{store}DataSource"), Value<string>(record, $"{store}DataSource"));
        }
        foreach (var field in new[] { "RuntimeDataSource", "PlacementDataSource", "CommandTransportDataSource" })
            Assert.Equal(Path.Combine(fixture.SharedRoot, "elsa.db"), Value<string>(record, field));
        Assert.Equal(Path.Combine(fixture.SharedRoot, "membership.db"), Value<string>(record, "MembershipDataSource"));
        Assert.Equal(Path.Combine(fixture.SharedRoot, "locks"), Value<string>(record, "LocksFolderPath"));
    }

    private static bool IsLive(JsonObject record, string key, string hostId) =>
        record[key]?.AsArray().Any(member => member?["HostId"]?.GetValue<string>() == hostId &&
            member["Status"]?.GetValue<string>() == "Active" && member["IsLive"]?.GetValue<bool>() == true &&
            member["IsDisplaced"]?.GetValue<bool>() == false) == true;
}
