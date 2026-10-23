using System.Globalization;
using System.Text.Json.Nodes;
using static Elsa.Workbench.Tests.NativeHostingScenario;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workbench.Tests;

/// <summary>Exercises a graceful two-process drain with the real Workbench SIGTERM lifecycle.</summary>
[Collection(WorkbenchBackgroundActivityRecoveryTests.Name)]
public sealed class NativeHostingGracefulHandoffTests(ITestOutputHelper output)
{
    private static readonly TimeSpan GateObservationLimit = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan HostStopLimit = TimeSpan.FromSeconds(29);

    [SkippableFact]
    public async Task Draining_owner_hands_off_accepted_execution_while_survivor_admits_work()
    {
        Skip.If(OperatingSystem.IsWindows(), "The fixture uses the Workbench SIGTERM lifecycle on POSIX.");

        await using var fixture = NativeHostingFixture.Create(NativeHostingProfile.Clustered, observeNativeHosting: true);
        fixture.HoldWorkerClaims("owner");
        var hosts = await fixture.SerialStartAsync(("owner", true), ("survivor", true));
        var owner = hosts[0];
        var survivor = hosts[1];
        var ownerId = $"{fixture.ScenarioId}-owner";
        var survivorId = $"{fixture.ScenarioId}-survivor";
        var journal = fixture.JournalDirectory;
        var acceptedJournal = Path.Combine(journal, "accepted");
        var admissionJournal = Path.Combine(journal, "admission");
        var holdPath = Path.Combine(journal, "graceful.stop.hold");
        var releasePath = Path.Combine(journal, "graceful.stop.release");
        var enteredPath = Path.Combine(journal, "graceful.stop.entered.owner");
        var timedOutPath = Path.Combine(journal, "graceful.stop.timed-out.owner");
        Task<TimeSpan?>? ownerStopping = null;

        try
        {
            Assert.NotEqual(owner.ProcessId, survivor.ProcessId);
            Assert.False(owner.HasExited);
            Assert.False(survivor.HasExited);
            AssertPinnedPackage(fixture, owner);
            AssertPinnedPackage(fixture, survivor);
            await LoginAsync(owner);
            await LoginAsync(survivor);

            var accepted = await PublishAndStartAsync(owner, acceptedJournal, "accepted before graceful drain");
            await WaitForStateAsync(owner, accepted.ExecutionId,
                state => ActivityStatus(state, "left") == "Running" && ActivityStatus(state, "right") == "Running");
            await WaitForAsync(survivor,
                () => StartedCount(acceptedJournal, "left") == 1 && StartedCount(acceptedJournal, "right") == 1,
                "both accepted background bodies to start on the survivor", () => DescribeJournal(acceptedJournal));
            var leftStart = Assert.Single(StartedLines(acceptedJournal, "left"));
            var rightStart = Assert.Single(StartedLines(acceptedJournal, "right"));
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture), leftStart.Split('|')[0]);
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture), rightStart.Split('|')[0]);

            // Free one of the two real worker slots while keeping the accepted workflow unfinished on its other body.
            // That lets the survivor admit a new background body during the drain without increasing fixture capacity.
            File.WriteAllText(Path.Combine(acceptedJournal, "left.release"), "release");
            await WaitForStateAsync(owner, accepted.ExecutionId,
                state => ActivityStatus(state, "left") == "Completed" && ActivityStatus(state, "right") == "Running");
            Assert.Equal(0, StartedCount(acceptedJournal, "final"));

            var originalFence = await WaitForEventAsync(journal, "ownership-acquired", accepted.ExecutionId,
                record => Value<int>(record, "ProcessId") == owner.ProcessId && Value<string>(record, "OwnerId") == ownerId,
                Patience);
            var originalPlacement = await WaitForEventAsync(journal, "placement-claimed", accepted.ExecutionId,
                record => Value<int>(record, "ProcessId") == owner.ProcessId && Value<string>(record, "PlacementOwnerId") == ownerId,
                Patience);
            var originalPlacementToken = Value<long>(originalPlacement, "PlacementToken");
            var originalPlacementExpiry = Value<DateTimeOffset>(originalPlacement, "PlacementExpiresAt");
            Assert.True(originalPlacementExpiry > DateTimeOffset.UtcNow);
            Assert.Equal(0, StartedCount(acceptedJournal, "final"));

            // The marker activates only this fixture's owner IBackgroundTask stop gate. SIGTERM still drives the
            // production membership transition to Draining before the ordinary hosted-service stop sequence.
            File.WriteAllText(holdPath, "hold");
            var stopRequestedAt = DateTimeOffset.UtcNow;
            var stopDeadline = stopRequestedAt + HostStopLimit;
            ownerStopping = owner.StopAsync(HostStopLimit);
            await WaitForGateEntryAsync(enteredPath, owner, GateObservationRemaining(stopDeadline));
            Assert.False(owner.HasExited);
            Assert.Equal(owner.ProcessId.ToString(CultureInfo.InvariantCulture), File.ReadAllText(enteredPath));

            var released = await WaitForEventAsync(journal, "placement-released", accepted.ExecutionId,
                record => Value<int>(record, "ProcessId") == owner.ProcessId &&
                          Value<string>(record, "PlacementOwnerId") == ownerId &&
                          Value<DateTimeOffset>(record, "RecordedAt") >= stopRequestedAt &&
                          Value<bool>(record, "ReleaseResult"),
                GateObservationRemaining(stopDeadline));
            // Admission and receipt handling may reclaim or renew placement before SIGTERM. Correlate the
            // lease actually released during shutdown rather than requiring the earliest historical token.
            var releasedToken = Value<long>(released, "PlacementToken");
            var releasedExpiry = Value<DateTimeOffset>(released, "PlacementExpiresAt");
            Assert.True(releasedToken >= originalPlacementToken);
            Assert.Contains(ReadEvents(journal), record =>
                record["Stage"]?.GetValue<string>() == "placement-claimed" &&
                record["ExecutionId"]?.GetValue<string>() == accepted.ExecutionId &&
                Value<int>(record, "ProcessId") == owner.ProcessId &&
                Value<string>(record, "ClaimantOwnerId") == ownerId &&
                Value<string>(record, "PlacementOwnerId") == ownerId &&
                Value<long>(record, "PlacementToken") == releasedToken &&
                Value<DateTimeOffset>(record, "RecordedAt") <= Value<DateTimeOffset>(released, "RecordedAt") &&
                Value<DateTimeOffset>(record, "PlacementExpiresAt") <= releasedExpiry);
            output.WriteLine(released.ToJsonString());
            Assert.Equal("Fresh", Value<string>(released, "FreshMembershipReadMode"));
            Assert.Null(released["FreshMembershipError"]);
            Assert.True(Value<DateTimeOffset>(released, "FreshMembershipJudgedAt") < releasedExpiry);
            Assert.True(Value<DateTimeOffset>(released, "RecordedAt") < releasedExpiry);
            AssertDrainingLive(Member(released, "FreshMembershipMembers", ownerId));
            AssertActiveLive(Member(released, "FreshMembershipMembers", survivorId));
            Assert.False(owner.HasExited);

            // The accepted instance remains readable from the live survivor while the old owner is held in shutdown.
            var acceptedWhileDraining = await WaitForStateAsync(survivor, accepted.ExecutionId,
                state => ActivityStatus(state, "left") == "Completed" && ActivityStatus(state, "right") == "Running",
                StopWindowRemaining(stopDeadline));
            Assert.Equal("Completed", ActivityStatus(acceptedWhileDraining, "left"));
            Assert.Equal("Running", ActivityStatus(acceptedWhileDraining, "right"));
            Assert.Equal(0, StartedCount(acceptedJournal, "final"));

            // This request is deliberately sent to the survivor. It proves the active process accepts and runs new
            // work during the observed drain; owning-layer DrainingTests cover refusal semantics for a drained member.
            var admission = await PublishAndStartAsync(survivor, admissionJournal, "admission during graceful drain");
            Assert.NotEqual(accepted.ArtifactId, admission.ArtifactId);
            Assert.False(owner.HasExited);
            Assert.False(ownerStopping.IsCompleted);
            await WaitForAsync(survivor,
                () => StartedCount(admissionJournal, "left") + StartedCount(admissionJournal, "right") == 1,
                "one new background body to start on the survivor during the held drain",
                () => DescribeJournal(admissionJournal), StopWindowRemaining(stopDeadline));
            var firstAdmissionBody = StartedCount(admissionJournal, "left") == 1 ? "left" : "right";
            var secondAdmissionBody = firstAdmissionBody == "left" ? "right" : "left";
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture),
                Assert.Single(StartedLines(admissionJournal, firstAdmissionBody)).Split('|')[0]);
            Assert.False(owner.HasExited);
            Assert.False(File.Exists(timedOutPath));
            output.WriteLine($"Admission {admission.ExecutionId}, artifact {admission.ArtifactId}, body {firstAdmissionBody} started on survivor {survivor.ProcessId} while owner {owner.ProcessId} was held in shutdown.");

            // The active survivor has admitted and started new work while the owner is still held in its drain.
            // Let graceful shutdown finish now so the bounded stop window is independent of body completion.
            Assert.False(ownerStopping.IsCompleted);
            File.WriteAllText(releasePath, "release");
            var stopElapsed = await ownerStopping.WaitAsync(HostStopLimit + TimeSpan.FromSeconds(2));
            Assert.NotNull(stopElapsed);
            Assert.True(stopElapsed < HostStopLimit, $"The owner took {stopElapsed} to stop after the test released the gate.");
            Assert.True(owner.HasExited);
            Assert.Equal(0, owner.ExitCode);
            Assert.False(File.Exists(timedOutPath));

            File.WriteAllText(Path.Combine(admissionJournal, firstAdmissionBody + ".release"), "release");
            await WaitForStateAsync(survivor, admission.ExecutionId,
                state => ActivityStatus(state, firstAdmissionBody) == "Completed");
            await WaitForAsync(survivor, () => StartedCount(admissionJournal, secondAdmissionBody) == 1,
                $"the second admitted body ({secondAdmissionBody}) to reach the survivor", () => DescribeJournal(admissionJournal));
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture),
                Assert.Single(StartedLines(admissionJournal, secondAdmissionBody)).Split('|')[0]);
            File.WriteAllText(Path.Combine(admissionJournal, secondAdmissionBody + ".release"), "release");
            var admissionCompleted = await WaitForStateAsync(survivor, admission.ExecutionId,
                state => state["instance"]?["status"]?.GetValue<string>() == "Completed");
            Assert.Empty(admissionCompleted["incidents"]!.AsArray());
            Assert.Equal(1, StartedCount(admissionJournal, "final"));
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture),
                Assert.Single(StartedLines(admissionJournal, "final")).Split('|')[0]);

            // The original accepted artifact and body records remain intact. Release its remaining right body
            // after the owner exits so the native parent must finish on the survivor.
            File.WriteAllText(Path.Combine(acceptedJournal, "right.release"), "release");
            var completed = await WaitForStateAsync(survivor, accepted.ExecutionId,
                state => state["instance"]?["status"]?.GetValue<string>() == "Completed");
            output.WriteLine(completed.ToJsonString());
            Assert.Empty(completed["incidents"]!.AsArray());
            Assert.Equal("Completed", ActivityStatus(completed, "left"));
            Assert.Equal("Completed", ActivityStatus(completed, "right"));
            Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "final");
            Assert.Equal(leftStart, Assert.Single(StartedLines(acceptedJournal, "left")));
            Assert.Equal(rightStart, Assert.Single(StartedLines(acceptedJournal, "right")));
            Assert.Equal(survivor.ProcessId.ToString(CultureInfo.InvariantCulture),
                Assert.Single(StartedLines(acceptedJournal, "final")).Split('|')[0]);

            await WaitForEventAsync(journal, "placement-claimed", accepted.ExecutionId,
                record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                          Value<string>(record, "PlacementOwnerId") == survivorId &&
                          Value<long>(record, "PlacementToken") > releasedToken,
                Patience);
            await WaitForEventAsync(journal, "ownership-acquired", accepted.ExecutionId,
                record => Value<int>(record, "ProcessId") == survivor.ProcessId &&
                          Value<string>(record, "OwnerId") == survivorId &&
                          Value<long>(record, "FencingToken") > Value<long>(originalFence, "FencingToken"),
                Patience);
            Assert.False(survivor.HasExited);
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            ReleaseBodies(acceptedJournal);
            ReleaseBodies(admissionJournal);
            if (ownerStopping is not null)
                await ownerStopping.WaitAsync(TimeSpan.FromSeconds(35));
        }
    }

    private static async Task WaitForGateEntryAsync(string path, WorkbenchProcess owner, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline && !owner.HasExited)
            await Task.Delay(50);

        Assert.True(File.Exists(path),
            $"The owner did not enter its graceful stop gate within {timeout}.\n{owner.Output}");
    }

    private static TimeSpan GateObservationRemaining(DateTimeOffset stopDeadline)
    {
        var remaining = StopWindowRemaining(stopDeadline);
        return remaining < GateObservationLimit ? remaining : GateObservationLimit;
    }

    private static TimeSpan StopWindowRemaining(DateTimeOffset stopDeadline)
    {
        var remaining = stopDeadline - DateTimeOffset.UtcNow;
        Assert.True(remaining > TimeSpan.Zero, "The graceful drain observation exceeded the owner's bounded stop window.");
        return remaining;
    }

    private static void AssertDrainingLive(JsonObject member)
    {
        Assert.Equal("Draining", Value<string>(member, "Status"));
        Assert.True(Value<bool>(member, "IsLive"));
        Assert.False(Value<bool>(member, "IsDisplaced"));
    }

    private static void AssertActiveLive(JsonObject member)
    {
        Assert.Equal("Active", Value<string>(member, "Status"));
        Assert.True(Value<bool>(member, "IsLive"));
        Assert.False(Value<bool>(member, "IsDisplaced"));
    }

    private static void ReleaseBodies(string journal)
    {
        if (!Directory.Exists(journal))
            return;
        foreach (var key in new[] { "left", "right" })
            File.WriteAllText(Path.Combine(journal, key + ".release"), "release");
    }
}
