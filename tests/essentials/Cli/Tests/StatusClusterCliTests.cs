using System.Diagnostics;
using Elsa.Cli.Worker;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// <c>persistence status</c> names the members that cannot read a pending version and lists the cluster's members through the
/// host's own closure (spec 181, FR-022), as the two-host demo's presenter runs it: the real tool, a packaged host that carries
/// the membership module and a module whose family the database's record has at 1 while this build reads 1 and 2, and a
/// SQLite database whose membership rows the tests write as a running host's provider would.
/// </summary>
public sealed class StatusClusterCliTests : IDisposable
{
    private const string Modules = "FeedModuleFixture,Cluster.Membership";
    private const string Family = "FeedModuleFixtureOrders";
    private const string ReadsOne = "1";
    private const string ReadsTwo = "2";

    /// <summary>Generous against the seconds the tool takes to start, so a member written as just heartbeated is live when it judges.</summary>
    private static readonly TimeSpan ExpiryPeriod = TimeSpan.FromSeconds(60);

    private readonly TempDirectory _directory = new("elsa-cli-status-cluster-");
    private readonly string _clusterHost = DotnetElsa.Host("ClusterHost");
    private readonly string _database;
    private readonly Dictionary<string, string> _env;

    public StatusClusterCliTests()
    {
        _database = _directory.File("elsa-status-cluster.db");
        _env = new() { ["ELSA_EF_CONNECTION"] = $"Data Source={_database};Pooling=False" };
        SeedRelease1();
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void A_pending_version_names_the_host_that_cannot_read_it_and_the_members_are_listed_beneath_the_family()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", reads: [ReadsOne]);
        Insert("foundation-host-b", reads: [ReadsOne, ReadsTwo]);

        var status = Status();

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains($"{Family} (FeedModuleFixture): finalized at 1; this host reads [1, 2]", status.Output, StringComparison.Ordinal);
        Assert.Contains("  2: pending, held by nothing; waits for every counted member to read it" + Environment.NewLine + "    waits for: foundation-host-a (reads 1)" + Environment.NewLine, status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("waits for: foundation-host-b", status.Output, StringComparison.Ordinal);
        Assert.Contains("members: 2 in Cluster.Membership, judged at ", status.Output, StringComparison.Ordinal);
        Assert.Contains(" with a skew allowance of 00:00:05", status.Output, StringComparison.Ordinal);
        Assert.Contains("foundation-host-a: Active, live, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.Contains($"    {Family}: reads 1{Environment.NewLine}", status.Output, StringComparison.Ordinal);
        Assert.Contains($"    {Family}: reads 1, 2{Environment.NewLine}", status.Output, StringComparison.Ordinal);
    }

    /// <summary>No member counted here blocks the version: said as that, not as a promise that it finalizes.</summary>
    [Fact]
    public void A_pending_version_no_counted_member_blocks_says_only_that()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", reads: [ReadsOne, ReadsTwo]);

        var status = Status();

        Assert.Contains("  2: pending, held by nothing; no member counted here blocks it", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("waits for:", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("finalizes", status.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The demo's two-member case: host-a was killed, and whether it still blocks depends on the skew allowance the hosts run
    /// with, so the tool judges with the one it is given and says which.
    /// </summary>
    [Theory]
    [InlineData("00:00:01", "expired", false)]
    [InlineData("00:00:30", "live", true)]
    public void A_member_that_stopped_heartbeating_blocks_only_within_the_skew_allowance_the_tool_is_given(string skew, string state, bool blocks)
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", reads: [ReadsOne], silent: ExpiryPeriod + TimeSpan.FromSeconds(2));
        Insert("foundation-host-b", reads: [ReadsOne, ReadsTwo]);

        var status = Status("--skew-allowance", skew);

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains($" with a skew allowance of {skew}", status.Output, StringComparison.Ordinal);
        Assert.Contains($"foundation-host-a: Active, {state}, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.Equal(blocks, status.Output.Contains("waits for: foundation-host-a (reads 1)", StringComparison.Ordinal));
        Assert.Contains(blocks ? "waits for every counted member to read it" : "no member counted here blocks it", status.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_that_left_is_listed_as_left_and_blocks_nothing()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", reads: [ReadsOne], status: "Left");

        var status = Status();

        Assert.Contains("foundation-host-a: Left, left, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.Contains("no member counted here blocks it", status.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row a newer provider wrote, in a form this build cannot interpret, counts as reading nothing while it may still be
    /// alive. Its status is one this build did not read, so it is not shown as Active.
    /// </summary>
    [Fact]
    public void A_member_whose_row_cannot_be_interpreted_blocks_the_version_and_its_status_is_not_claimed()
    {
        Apply(_clusterHost, Modules);
        Insert("newer-host", status: "Serving", schemaVersion: "2.0.0", report: """{"sections":[{"kind":"placement"}]}""");

        var status = Status();

        Assert.Contains("waits for: newer-host (reports what it reads in a form this tool cannot interpret; counts as reading nothing)", status.Output, StringComparison.Ordinal);
        Assert.Contains("newer-host: unknown, live, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("newer-host: Active", status.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_displaced_incarnation_that_is_still_live_says_a_later_one_replaced_it()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", reads: [ReadsOne, ReadsTwo], current: false);

        var status = Status();

        Assert.Contains("foundation-host-a: Active, live, displaced by a later incarnation, last heartbeat", status.Output, StringComparison.Ordinal);
    }

    /// <summary>A live member that says nothing about the family is not counted for it, and the line says so instead of "reads".</summary>
    [Fact]
    public void A_live_member_that_reports_nothing_about_the_family_is_not_counted_for_it()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", report: ReportJson([]));

        var status = Status();

        Assert.Contains($"    {Family}: reports nothing, so it is not counted for it", status.Output, StringComparison.Ordinal);
        Assert.Contains("no member counted here blocks it", status.Output, StringComparison.Ordinal);
    }

    /// <summary>An entry that speaks for another database than the family's is no part of what the member reads of it.</summary>
    [Fact]
    public void A_members_entry_for_another_database_is_left_out_of_what_it_reads_of_the_family()
    {
        Apply(_clusterHost, Modules);
        Insert("foundation-host-a", report: ReportJson([Entry([ReadsOne, ReadsTwo], database: "another-database")]));

        var status = Status();

        Assert.Contains($"    {Family}: reports nothing, so it is not counted for it", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Family}: reads 1, 2", status.Output, StringComparison.Ordinal);
    }

    /// <summary>The demo's single-host case: membership is in the closure but this database never had its table created.</summary>
    [Fact]
    public void A_database_without_the_membership_table_is_a_cluster_of_one_and_leaves_the_pending_line_as_it_was()
    {
        Apply(_clusterHost, "FeedModuleFixture");

        var status = Status();

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: 'Cluster.Membership' has migrations not applied in this database, so it holds no members: a cluster of one.", status.Output, StringComparison.Ordinal);
        Assert.Contains("  2: pending, held by nothing; waits for every counted member to read it", status.Output, StringComparison.Ordinal);
    }

    /// <summary>A host whose closure carries no membership provider says so by the explicit marker, and only then is it "this host only".</summary>
    [Fact]
    public void A_host_that_carries_no_membership_provider_says_this_host_only()
    {
        var host = DotnetElsa.Host("Host");
        Apply(host, "Secrets");

        var status = Run(host, "status", "Secrets");

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: this host only (this host's closure carries no cluster membership provider).", status.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host whose tooling predates the cluster answers with no <c>cluster</c> at all, which says nothing about the cluster,
    /// and is never sent a skew allowance its closed contract would refuse.
    /// </summary>
    [Fact]
    public void A_host_whose_tooling_reports_no_cluster_is_not_said_to_be_alone_and_is_not_sent_a_skew_allowance()
    {
        var marker = _directory.File("request.json");
        _env["ELSA_LEGACY_TOOLING_MARKER"] = marker;

        var status = Run(DotnetElsa.Host("LegacyHost"), "status", "Secrets", "--skew-allowance", "00:00:02");

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: cluster membership not reported by this host's tooling.", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("this host only", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("skewAllowance", File.ReadAllText(marker), StringComparison.Ordinal);
    }

    /// <summary>The skew a host configures is the one the tool judges with, unless it is told another.</summary>
    [Fact]
    public void The_skew_allowance_defaults_to_the_hosts_configured_one_and_the_flag_overrides_it()
    {
        using var host = new TempDirectory("elsa-cli-status-cluster-host-");
        CopyDirectory(_clusterHost, host.Path);
        File.WriteAllText(host.File("appsettings.json"), """{ "Elsa": { "Cluster": { "Membership": { "SkewAllowance": "00:00:02" } } } }""");
        File.WriteAllText(host.File("appsettings.Staging.json"), """{ "Elsa": { "Cluster": { "Membership": { "SkewAllowance": "00:00:03" } } } }""");
        Apply(host.Path, Modules);
        Insert("foundation-host-a", reads: [ReadsOne, ReadsTwo]);

        Assert.Contains(" with a skew allowance of 00:00:02", Run(host.Path, "status", Modules).Output, StringComparison.Ordinal);
        Assert.Contains(" with a skew allowance of 00:00:03", Run(host.Path, "status", Modules, "--environment", "Staging").Output, StringComparison.Ordinal);
        Assert.Contains(" with a skew allowance of 00:00:09", Run(host.Path, "status", Modules, "--skew-allowance", "00:00:09").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skew_allowance_that_is_not_a_non_negative_time_span_is_refused()
    {
        Apply(_clusterHost, Modules);

        var refused = Status("--skew-allowance", "-00:00:02");

        Assert.Equal(ToolExitCode.Refusal, refused.ExitCode);
        Assert.Contains("invalid-skew-allowance", refused.Text, StringComparison.Ordinal);
    }

    private CliRun Status(params string[] extra) => Run(_clusterHost, "status", Modules, ["--family", Family, .. extra]);

    private CliRun Apply(string host, string modules)
    {
        var apply = Run(host, "apply", modules);
        Assert.Equal(ToolExitCode.Success, apply.ExitCode);
        return apply;
    }

    private CliRun Run(string host, string command, string modules, params string[] extra) =>
        DotnetElsa.Run(_env, ["persistence", command, "--host", host, "--provider", "Sqlite", "--modules", modules, .. extra]);

    /// <summary>The database as the release before this one left it: the module's finalization record at 1, made by the fixture host, which no migration of the module creates.</summary>
    private void SeedRelease1()
    {
        var start = new ProcessStartInfo(DotnetMuxer.Path()) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "exec", Path.Join(_clusterHost, "Elsa.Cli.Fixtures.ClusterHost.dll"), "seed-release-1", _env["ELSA_EF_CONNECTION"] })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    /// <summary>
    /// One member's row as a running host's provider writes it: current, active and just heartbeated unless said otherwise,
    /// with the readability report it publishes.
    /// </summary>
    private void Insert(string hostId, string[]? reads = null, string status = "Active", TimeSpan silent = default, bool current = true, string schemaVersion = "1.0.0", string? report = null)
    {
        using var connection = new SqliteConnection($"Data Source={_database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO elsa_cluster_members (HostId, Incarnation, CurrentHostId, Status, HeartbeatAtUtcTicks, ExpiryPeriodTicks, LeftAtUtcTicks, ReportJson, ReportRevision, Revision, SchemaVersion)
            VALUES ($host, $incarnation, $current, $status, $heartbeat, $expiry, NULL, $report, 1, 1, $schemaVersion)
            """;
        command.Parameters.AddWithValue("$host", hostId);
        command.Parameters.AddWithValue("$incarnation", $"{hostId}-1");
        command.Parameters.AddWithValue("$current", current ? hostId : DBNull.Value);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$heartbeat", (DateTimeOffset.UtcNow - silent).UtcTicks);
        command.Parameters.AddWithValue("$expiry", ExpiryPeriod.Ticks);
        command.Parameters.AddWithValue("$report", report ?? ReportJson(reads is null ? [] : [Entry(reads)]));
        command.Parameters.AddWithValue("$schemaVersion", schemaVersion);
        command.ExecuteNonQuery();
    }

    private static string Entry(string[] reads, string? database = null) =>
        $$"""{"family":"{{Family}}","efModule":"FeedModuleFixture","readableVersions":[{{string.Join(",", reads.Select(version => $"\"{version}\""))}}],"databaseIdentity":{{(database is null ? "null" : $"\"{database}\"")}},"observedFinalizedVersion":null}""";

    private static string ReportJson(string[] entries) => $$"""{"readability":{"entries":[{{string.Join(",", entries)}}]},"runnability":null}""";

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Join(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
