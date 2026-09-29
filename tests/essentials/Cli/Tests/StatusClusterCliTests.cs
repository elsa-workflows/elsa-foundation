using Elsa.Cli.Worker;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// <c>persistence status</c> lists the cluster's members through the host's own closure (spec 181, FR-022), as the two-host
/// demo's presenter runs it: the real tool, a packaged host that carries the membership module, and a SQLite database two
/// hosts have written their entries to.
/// </summary>
public sealed class StatusClusterCliTests : IDisposable
{
    private const string Family = "Secrets";

    private readonly TempDirectory _directory = new("elsa-cli-status-cluster-");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Status_lists_each_member_with_its_status_heartbeat_and_the_versions_it_reads()
    {
        var (env, database) = NewDatabase();
        Assert.Equal(ToolExitCode.Success, Run(env, "apply", "Secrets,Cluster.Membership").ExitCode);
        Insert(database, "foundation-host-a", reads: ["1.0.0"]);
        Insert(database, "foundation-host-b", reads: ["1.0.0", "2.0.0"]);

        var status = Run(env, "status", "Secrets,Cluster.Membership", "--family", Family);

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: 2 in Cluster.Membership", status.Output, StringComparison.Ordinal);
        Assert.Contains("foundation-host-a: Active, counted, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.Contains("foundation-host-b: Active, counted, last heartbeat", status.Output, StringComparison.Ordinal);
        Assert.Contains("Secrets: reads 1.0.0" + Environment.NewLine, status.Output, StringComparison.Ordinal);
        Assert.Contains("Secrets: reads 1.0.0, 2.0.0" + Environment.NewLine, status.Output, StringComparison.Ordinal);
    }

    /// <summary>The demo's single-host case: membership is in the closure but this database never had its table created.</summary>
    [Fact]
    public void Status_says_a_database_without_the_membership_table_is_a_cluster_of_one()
    {
        var (env, _) = NewDatabase();
        Assert.Equal(ToolExitCode.Success, Run(env, "apply", "Secrets").ExitCode);

        var status = Run(env, "status", "Secrets");

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: 'Cluster.Membership' has migrations not applied in this database, so it holds no members: a cluster of one.", status.Output, StringComparison.Ordinal);
    }

    /// <summary>A host whose closure has no membership at all is a cluster of one, and says so without a members section.</summary>
    [Fact]
    public void Status_of_a_host_that_carries_no_membership_says_this_host_only()
    {
        var (env, _) = NewDatabase();
        string[] target = ["--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets"];
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run(env, ["persistence", "apply", .. target]).ExitCode);

        var status = DotnetElsa.Run(env, ["persistence", "status", .. target]);

        Assert.Equal(ToolExitCode.Success, status.ExitCode);
        Assert.Contains("members: this host only", status.Output, StringComparison.Ordinal);
    }

    private (Dictionary<string, string> Env, string Database) NewDatabase()
    {
        var database = Path.Join(_directory.Path, "elsa-status-cluster.db");
        return (new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = $"Data Source={database};Pooling=False" }, database);
    }

    private static CliRun Run(Dictionary<string, string> env, string command, string modules, params string[] extra) =>
        DotnetElsa.Run(env, ["persistence", command, "--host", DotnetElsa.Host("ClusterHost"), "--provider", "Sqlite", "--modules", modules, .. extra]);

    /// <summary>One current, active, just-heartbeated member as a running host's provider writes it, with the readability report it publishes.</summary>
    private static void Insert(string database, string hostId, string[] reads)
    {
        var versions = string.Join(",", reads.Select(version => $"\"{version}\""));
        var report = $$"""{"readability":{"entries":[{"family":"{{Family}}","efModule":"Secrets","readableVersions":[{{versions}}],"databaseIdentity":null,"observedFinalizedVersion":null}]},"runnability":null}""";
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO elsa_cluster_members (HostId, Incarnation, CurrentHostId, Status, HeartbeatAtUtcTicks, ExpiryPeriodTicks, LeftAtUtcTicks, ReportJson, ReportRevision, Revision, SchemaVersion)
            VALUES ($host, $incarnation, $host, 'Active', $heartbeat, $expiry, NULL, $report, 1, 1, '1.0.0')
            """;
        command.Parameters.AddWithValue("$host", hostId);
        command.Parameters.AddWithValue("$incarnation", $"{hostId}-1");
        command.Parameters.AddWithValue("$heartbeat", DateTimeOffset.UtcNow.UtcTicks);
        command.Parameters.AddWithValue("$expiry", TimeSpan.FromMinutes(5).Ticks);
        command.Parameters.AddWithValue("$report", report);
        command.ExecuteNonQuery();
    }
}
