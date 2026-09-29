using System.Text.Json;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// What <c>persistence status</c> prints (spec 181, FR-022), rendered from the JSON the host's tooling answers with: under a
/// pending version, the hosts it waits for as host id and the versions each reads, and the cluster's members. The two-host
/// demo's shape: the family finalized at 1.0.0, this build reading [1.0.0, 2.0.0], one host not yet on the release that
/// reads 2.0.0.
/// </summary>
public sealed class StatusReportTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void A_pending_version_names_each_host_it_waits_for_and_the_members_are_listed_beneath_the_families()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: [new { hostId = "foundation-host-a", incarnation = "a1", reportReadable = true, reads = new[] { "1.0.0" } }]), Cluster(
            Member("foundation-host-a", "1.0.0"),
            Member("foundation-host-b", "1.0.0", "2.0.0")));

        Assert.Equal(
            """
            SamplesNotes (SamplesNotes): finalized at 1.0.0; this host reads [1.0.0, 2.0.0]
              2.0.0: pending, held by nothing; waits for every counted member to read it
                waits for: foundation-host-a (reads 1.0.0)

            members: 2 in Cluster.Membership, judged at 2026-09-29T12:00:00+00:00 with a skew allowance of 00:00:05
              foundation-host-a: Active, counted, last heartbeat 2026-09-29T11:59:58+00:00
                SamplesNotes: reads 1.0.0
              foundation-host-b: Active, counted, last heartbeat 2026-09-29T11:59:58+00:00
                SamplesNotes: reads 1.0.0, 2.0.0

            1 family(ies).

            """.ReplaceLineEndings(),
            output.ReplaceLineEndings());
    }

    [Fact]
    public void A_pending_version_no_counted_member_blocks_says_it_finalizes_at_the_next_evaluation()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: []), Cluster(Member("foundation-host-b", "1.0.0", "2.0.0")));

        Assert.Contains("2.0.0: pending, held by nothing; every counted member reads it, so it finalizes at the next evaluation", output, StringComparison.Ordinal);
        Assert.DoesNotContain("waits for:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_whose_report_this_tool_cannot_interpret_is_named_as_reading_nothing()
    {
        var output = Render(
            WorkerCommands.Status,
            Family(waitsFor: [new { hostId = "newer-host", incarnation = "n1", reportReadable = false, reads = Array.Empty<string>() }]),
            Cluster(new { hostId = "newer-host", incarnation = "n1", status = "Active", live = true, displaced = false, lastHeartbeatAt = Heartbeat, reportReadable = false, reads = Array.Empty<object>() }));

        Assert.Contains("waits for: newer-host (reports what it reads in a form this tool cannot interpret; counts as reading nothing)", output, StringComparison.Ordinal);
        Assert.Contains("newer-host: Active, counted", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_that_left_or_expired_is_listed_and_says_it_is_not_counted()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: []), Cluster(
            Member("host-left", status: "Left", live: false, versions: ["1.0.0"]),
            Member("host-gone", status: "Active", live: false, versions: ["1.0.0"])));

        Assert.Contains("host-left: Left, left, last heartbeat", output, StringComparison.Ordinal);
        Assert.Contains("host-gone: Active, expired, last heartbeat", output, StringComparison.Ordinal);
    }

    /// <summary>An entry that speaks for another database than the family's is no part of what the member reads of it.</summary>
    [Fact]
    public void A_members_entry_for_another_database_is_left_out_of_the_family_it_does_not_speak_for()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: []), Cluster(new
        {
            hostId = "host-a", incarnation = "a1", status = "Active", live = true, displaced = false, lastHeartbeatAt = Heartbeat, reportReadable = true,
            reads = new[]
            {
                new { family = "SamplesNotes", databaseIdentity = (string?)"another-database", versions = new[] { "9.0.0" } },
                new { family = "SamplesNotes", databaseIdentity = (string?)"this-database", versions = new[] { "1.0.0" } }
            }
        }));

        Assert.Contains("SamplesNotes: reads 1.0.0" + Environment.NewLine, output, StringComparison.Ordinal);
        Assert.DoesNotContain("9.0.0", output, StringComparison.Ordinal);
    }

    /// <summary>A cluster of one: no membership provider in the host's closure, so nothing is claimed about who else is running.</summary>
    [Fact]
    public void Without_membership_the_pending_version_keeps_its_line_and_the_cluster_is_this_host_only()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: null), cluster: null);

        Assert.Contains("  2.0.0: pending, held by nothing; waits for every counted member to read it" + Environment.NewLine, output, StringComparison.Ordinal);
        Assert.DoesNotContain("waits for:", output, StringComparison.Ordinal);
        Assert.Contains("members: this host only", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_membership_table_that_could_not_be_read_says_why_instead_of_listing_members()
    {
        var output = Render(WorkerCommands.Status, Family(waitsFor: null), new { module = "Cluster.Membership", note = "'Cluster.Membership' has migrations not applied in this database, so it holds no members: a cluster of one.", members = Array.Empty<object>() });

        Assert.Contains("members: 'Cluster.Membership' has migrations not applied in this database", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Hold_and_release_print_no_member_section()
    {
        var output = Render(WorkerCommands.Hold, Family(waitsFor: null), cluster: null);

        Assert.DoesNotContain("members:", output, StringComparison.Ordinal);
        Assert.Contains("Hold placed.", output, StringComparison.Ordinal);
    }

    private const string Heartbeat = "2026-09-29T11:59:58+00:00";

    private static object Member(string hostId, params string[] versions) => Member(hostId, "Active", true, versions);

    private static object Member(string hostId, string status, bool live, string[] versions) => new
    {
        hostId, incarnation = hostId + "-1", status, live, displaced = false, lastHeartbeatAt = Heartbeat, reportReadable = true,
        reads = new[] { new { family = "SamplesNotes", databaseIdentity = (string?)null, versions } }
    };

    private static object Cluster(params object[] members) => new
    {
        module = "Cluster.Membership", judgedAt = "2026-09-29T12:00:00+00:00", skewAllowance = "00:00:05", note = (string?)null, members
    };

    private static object Family(object[]? waitsFor) => new
    {
        module = "SamplesNotes", family = "SamplesNotes", databaseIdentity = "this-database", finalizedVersion = "1.0.0",
        readableVersions = new[] { "1.0.0", "2.0.0" }, intent = (object?)null, holds = Array.Empty<object>(),
        pending = new[] { new { version = "2.0.0", state = "pending", heldBy = Array.Empty<string>(), waitsFor } },
        completionVersion = (string?)null, backfillRun = (object?)null, completionWithdrawn = (object?)null
    };

    private static string Render(string command, object family, object? cluster)
    {
        using var document = JsonSerializer.SerializeToDocument(new { provider = "PostgreSql", schema = (string?)null, families = new[] { family }, cluster }, Json);
        var output = new StringWriter();
        Report.WriteFinalization(output, command, document.RootElement);
        return output.ToString();
    }
}
