using System.Text;
using System.Text.Json;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Testing;
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

        Assert.Equal([Older, Newer], fleet.Members.Select(member => member.HostId));
        Assert.All(fleet.Members, member => Assert.True(member is { Status: "Active", Live: true, Displaced: false, ReportReadable: true }));
        Assert.All(fleet.Members, member => Assert.Equal(_fixture.Clock.GetUtcNow(), member.LastHeartbeatAt));
        Assert.Equal([["1"], ["1", "2"]], fleet.Members.Select(member => Assert.Single(member.Reads, reads => reads.Family == Family).Versions.ToArray()));
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

        var left = Assert.Single(fleet.Members, member => member.HostId == Older);
        Assert.True(left is { Status: "Left", Live: false });
        var unreadable = Assert.Single(fleet.Blockers(Family, databaseIdentity: null, "2"));
        Assert.Equal(("host-c", false), (unreadable.HostId, unreadable.ReportReadable));
        Assert.False(Assert.Single(fleet.Members, member => member.HostId == "host-c").ReportReadable);
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
        Assert.Equal(JsonValueKind.Null, cluster.GetProperty("note").ValueKind);
        Assert.Equal([Older, Newer], cluster.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("hostId").GetString()));
    }

    private async Task<IConformanceMember> StartAsync(string hostId, params string[] versions) =>
        await _fixture.StartMemberAsync(new ConformanceMemberSetup(hostId, [new ReadabilityEntry(Family, "Notes.Module", versions)]));

    private async Task<EfToolingFleet> ReadAsync() =>
        await _fixture.WithStoreAsync(context => new EfClusterMembershipToolingSource(_fixture.Clock).ReadAsync(context));
}
