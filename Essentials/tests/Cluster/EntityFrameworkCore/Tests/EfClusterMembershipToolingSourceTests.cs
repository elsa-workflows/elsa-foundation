using System.Text;
using System.Text.Json;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Readability;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework.Tooling;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// What <c>dotnet elsa persistence status</c> reads of the fleet (spec 181, FR-022): over a real membership table, two hosts
/// that read different versions of a family, the pending version names the host that cannot read it yet, with the versions
/// it does read, and every member of the table is listed with what it reads. The members are the provider's own, so the
/// table is what a running two-host cluster leaves.
/// </summary>
public sealed class EfClusterMembershipToolingSourceTests : IAsyncDisposable
{
    private const string Family = "Notes";
    private const string Older = "host-a";
    private const string Newer = "host-b";
    private const string ThisDatabase = "this-database";
    private const string AnotherDatabase = "another-database";

    private static readonly EfToolingFamilyDatabase[] Families = [new(Family, null)];

    private readonly EfClusterMembershipTestStore _store = EfClusterMembershipTestStore.CreateSqlite();
    private readonly EfClusterMembershipConformanceFixture _fixture;

    public EfClusterMembershipToolingSourceTests() => _fixture = new(_store);

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_pending_version_names_the_member_that_cannot_read_it_and_the_versions_it_does()
    {
        await StartAsync(Older, "1");
        await StartAsync(Newer, "1", "2");

        var fleet = await ReadAsync();

        var waiting = Assert.Single(fleet.Blockers(Family, databaseIdentity: null, "2"));
        Assert.Equal((Older, true), (waiting.HostId, waiting.ReportReadable));
        Assert.Equal(["1"], waiting.Reads);
        Assert.Empty(fleet.Blockers(Family, databaseIdentity: null, "1"));
    }

    [Fact]
    public async Task Every_member_is_listed_with_its_status_liveness_heartbeat_and_the_versions_it_reads()
    {
        await StartAsync(Older, "1");
        await StartAsync(Newer, "1", "2");

        var fleet = await ReadAsync();

        var members = fleet.MembersFor(Families);
        Assert.Equal([Older, Newer], members.Select(member => member.HostId));
        Assert.All(members, member => Assert.True(member is { Status: "Active", Live: true, Displaced: false, ReportReadable: true }));
        Assert.All(members, member => Assert.Equal(_fixture.Clock.GetUtcNow(), member.LastHeartbeatAt));
        Assert.Equal([["1"], ["1", "2"]], members.Select(member => Assert.Single(member.Reads, reads => reads.Family == Family).Versions.ToArray()));
    }

    /// <summary>
    /// A member that left is listed, and never counted; one whose entry this build cannot interpret is listed and blocks every
    /// version, as it does the gate's own count (spec 183, FR-012).
    /// </summary>
    [Fact]
    public async Task A_member_that_left_is_listed_but_blocks_nothing_and_an_uninterpretable_one_blocks_every_version()
    {
        var older = await StartAsync(Older, "1");
        await StartAsync(Newer, "1", "2");
        await older.StopAsync();
        await _fixture.PlantUninterpretableEntryAsync("host-c");

        var fleet = await ReadAsync();

        var members = fleet.MembersFor(Families);
        var left = Assert.Single(members, member => member.HostId == Older);
        Assert.True(left is { Status: "Left", Live: false });
        var unreadable = Assert.Single(fleet.Blockers(Family, databaseIdentity: null, "2"));
        Assert.Equal(("host-c", false), (unreadable.HostId, unreadable.ReportReadable));
        var planted = Assert.Single(members, member => member.HostId == "host-c");
        Assert.False(planted.ReportReadable);
        Assert.Equal((EfToolingClusterMember.UnknownStatus, true), (planted.Status, planted.Live));
    }

    /// <summary>The tool's own answer, through the host's entry point: the members of the table, found beside the module.</summary>
    [Fact]
    public async Task Status_lists_the_members_from_the_membership_table_through_the_tooling_entry_point()
    {
        await StartAsync(Older, "1");
        await StartAsync(Newer, "1", "2");

        var request = new { version = 1, command = "status", provider = "Sqlite", selection = new { kind = "modules", modules = new[] { ClusterMembershipEfModule.Name } }, connection = _store.ConnectionString };
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, [typeof(ClusterMembershipEfModule).Assembly]);
        using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        var cluster = response.RootElement.GetProperty("finalization").GetProperty("cluster");
        Assert.Equal(EfToolingClusterAvailability.Read, cluster.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, cluster.GetProperty("note").ValueKind);
        Assert.Equal([Older, Newer], cluster.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("hostId").GetString()));
    }

    /// <summary>The boundary instant counts: a member is live until its heartbeat plus its expiry plus the skew allowance has passed.</summary>
    [Theory]
    [InlineData(2, 30 + 2, true)]
    [InlineData(2, 30 + 2 + 1, false)]
    [InlineData(5, 30 + 5 + 1, false)]
    [InlineData(5, 30 + 2 + 1, true)]
    public async Task Liveness_is_judged_with_the_skew_allowance_the_tool_is_given(int skewSeconds, int silentSeconds, bool live)
    {
        await _fixture.WriteRowAsync(_fixture.NewRow(Older) with { HeartbeatAtUtcTicks = (_fixture.Clock.GetUtcNow() - TimeSpan.FromSeconds(silentSeconds)).UtcTicks });

        var fleet = await ReadAsync(TimeSpan.FromSeconds(skewSeconds));

        Assert.Equal(TimeSpan.FromSeconds(skewSeconds), fleet.SkewAllowance);
        Assert.Equal(live, Assert.Single(fleet.MembersFor(Families)).Live);
    }

    [Fact]
    public async Task The_provider_default_skew_allowance_is_used_when_the_tool_names_none()
    {
        var fleet = await ReadAsync(skewAllowance: null);

        Assert.Equal(new Elsa.Cluster.Core.Options.ClusterMembershipOptions().SkewAllowance, fleet.SkewAllowance);
    }

    /// <summary>
    /// The tool and the gate answer the same question of the same table: the members the tool names as waiting are the ones
    /// the counting query the gate asks (<see cref="ClusterSchemaFleet"/>, over the provider's own fleet view) fails, for
    /// members live, expired within and beyond the skew allowance, left, displaced, uninterpretable, and reporting for one
    /// database, another, or every one, and for a member that reports nothing about the family or cannot say what it reads.
    /// </summary>
    [Fact]
    public async Task The_members_the_tool_waits_for_are_the_members_the_gate_counts_as_not_reading_the_version()
    {
        var timings = _fixture.Timings;
        await PlantAsync("live-reads-1", reads: ["1"]);
        await PlantAsync("live-reads-1-and-2", reads: ["1", "2"]);
        await PlantAsync("expired-beyond-skew", reads: ["1"], silent: timings.ExpiryPeriod + timings.SkewAllowance + TimeSpan.FromSeconds(1));
        await PlantAsync("expired-within-skew", reads: ["1"], silent: timings.ExpiryPeriod + timings.SkewAllowance);
        await PlantAsync("left", reads: ["1"], alter: row => row with { Status = nameof(MemberStatus.Left), LeftAtUtcTicks = row.HeartbeatAtUtcTicks });
        await PlantAsync("displaced", reads: ["1"], alter: row => row with { CurrentHostId = null });
        await PlantAsync("this-database", reads: ["1"], database: ThisDatabase);
        await PlantAsync("another-database", reads: ["1"], database: AnotherDatabase);
        await PlantAsync("reports-nothing");
        await PlantAsync("reports-no-section", alter: row => row with { ReportJson = """{"readability":null,"runnability":null}""" });
        await _fixture.PlantUninterpretableEntryAsync("uninterpretable");
        var observer = await _fixture.PrepareMemberAsync(new ConformanceMemberSetup("observer", null));
        var gate = new ClusterSchemaFleet(observer.Membership);
        var fleet = await ReadAsync(timings.SkewAllowance);

        foreach (var database in new string?[] { null, ThisDatabase, AnotherDatabase })
        foreach (var version in new[] { "1", "2" })
        {
            var counting = (await observer.Membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, version, database)), FleetReadMode.Fresh))
                .Failures.Select(failure => failure.Member.HostId).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(counting, HostsWaitedFor(fleet, database, version));
            if (database is not null)
            {
                var answer = await gate.CountAsync(Family, version, database);
                Assert.Equal(counting, answer.Blockers.Select(blocker => blocker.Split(' ')[0]).Order(StringComparer.Ordinal).ToArray());
                Assert.Equal(counting.Length == 0, answer.EveryCountedMemberReads);
            }
        }

        Assert.Equal(
            ["displaced", "expired-within-skew", "live-reads-1", "reports-no-section", "this-database", "uninterpretable"],
            HostsWaitedFor(fleet, ThisDatabase, "2"));
    }

    private static string[] HostsWaitedFor(EfToolingFleet fleet, string? database, string version) =>
        [.. fleet.Blockers(Family, database, version).Select(waiting => waiting.HostId).Order(StringComparer.Ordinal)];

    /// <summary>A current, live entry written directly, reporting that it reads <paramref name="reads"/> of the family (for one database, or every) or, with none, nothing about it.</summary>
    private async Task PlantAsync(string hostId, string[]? reads = null, string? database = null, TimeSpan silent = default, Func<ClusterMemberRow, ClusterMemberRow>? alter = null)
    {
        var entries = reads is null
            ? ""
            : $$"""{"family":"{{Family}}","efModule":"Notes.Module","readableVersions":[{{string.Join(",", reads.Select(version => $"\"{version}\""))}}],"databaseIdentity":{{(database is null ? "null" : $"\"{database}\"")}},"observedFinalizedVersion":null,"moduleActive":true}""";
        var row = _fixture.NewRow(hostId) with
        {
            HeartbeatAtUtcTicks = (_fixture.Clock.GetUtcNow() - silent).UtcTicks,
            ReportJson = $$"""{"readability":{"entries":[{{entries}}]},"runnability":null}"""
        };
        await _fixture.WriteRowAsync(alter?.Invoke(row) ?? row);
    }

    private async Task<IConformanceMember> StartAsync(string hostId, params string[] versions) =>
        await _fixture.StartMemberAsync(new ConformanceMemberSetup(hostId, [new ReadabilityEntry(Family, "Notes.Module", versions)]));

    private async Task<EfToolingFleet> ReadAsync(TimeSpan? skewAllowance = null) =>
        await _fixture.WithStoreAsync(context => new EfClusterMembershipToolingSource(_fixture.Clock).ReadAsync(context, skewAllowance));
}
