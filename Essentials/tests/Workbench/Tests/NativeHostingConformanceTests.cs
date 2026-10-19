using System.Security.Cryptography;
using System.Text.Json.Nodes;
using static Elsa.Workbench.Tests.NativeHostingScenario;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>Exercises native routing and continuation through two real processes and shared EF stores.</summary>
[Collection(WorkbenchBackgroundActivityRecoveryTests.Name)]
public sealed class NativeHostingConformanceTests
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

    private static void AssertPinnedPackage(NativeHostingFixture fixture, WorkbenchProcess host)
    {
        var copiedPackage = Path.Combine(host.ContentRoot, "packages", Path.GetFileName(fixture.PackagePath));
        Assert.Equal(fixture.PackageSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copiedPackage))));
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

    private static JsonObject Member(JsonObject record, string key, string hostId) =>
        Assert.Single(record[key]!.AsArray().Select(node => node!.AsObject()), member => Value<string>(member, "HostId") == hostId);

    private static bool IsLive(JsonObject record, string key, string hostId) =>
        record[key]?.AsArray().Any(member => member?["HostId"]?.GetValue<string>() == hostId &&
            member["Status"]?.GetValue<string>() == "Active" && member["IsLive"]?.GetValue<bool>() == true &&
            member["IsDisplaced"]?.GetValue<bool>() == false) == true;

    private static T Value<T>(JsonObject record, string key) => record[key]!.GetValue<T>();

    private static async Task<JsonObject> WaitForEventAsync(WorkbenchProcess host, string journal, string stage,
        string? executionId, Func<JsonObject, bool> predicate)
    {
        JsonObject? found = null;
        await WaitForAsync(host, () =>
        {
            found = ReadEvents(journal).FirstOrDefault(record => Value<string>(record, "Stage") == stage &&
                (executionId is null || record["ExecutionId"]?.GetValue<string>() == executionId) && predicate(record));
            return found is not null;
        }, $"native hosting event '{stage}' for {executionId ?? "composition"}", () =>
        {
            var evidence = string.Join(Environment.NewLine, ReadEvents(journal).TakeLast(12).Select(record => record.ToJsonString()));
            return evidence.Length <= 12000 ? evidence : evidence[..12000] + "… native hosting evidence truncated";
        });
        return found!;
    }

    private static IEnumerable<JsonObject> ReadEvents(string journal) =>
        Directory.EnumerateFiles(journal, "native-hosting.*.json")
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject())
            .OrderBy(record => Value<DateTimeOffset>(record, "RecordedAt"));
}
