using System.Text.Json.Nodes;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Cluster.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// A membership report is read through the family's chain (spec 180, FR-009), and an upcaster that fails on a readable
/// report makes the entry uninterpretable rather than corrupt: spec 183, FR-012 takes precedence over spec 180, FR-009
/// for membership. Uninterpretable is what <c>StoredMember</c> turns into a counted entry with
/// <see cref="MemberReport.Unknown"/>, as the store tests show for a report this build cannot parse.
/// </summary>
/// <remarks>
/// Membership has only ever had one version, so its own chain cannot hold an older report. These tests drive
/// <see cref="StoredMember.ReadReport"/>, the report read the store uses, directly over chains of their own (§2.23.3).
/// </remarks>
public sealed class ClusterMembershipReportUpcastTests
{
    private const string Report =
        """{"readability":{"entries":[{"family":"F","efModule":"M","readableVersions":["1"],"databaseIdentity":null,"observedFinalizedVersion":null}]},"runnability":null}""";

    /// <summary>
    /// The decision under test, in both directions that would look like success: a throw would fail the whole membership
    /// read, and reading the stored report as if it were current would credit the member with a report it never
    /// published in this format. A step that drops the report column fails the row as surely as one that throws (#2144).
    /// </summary>
    [Theory]
    [InlineData(typeof(Failing))]
    [InlineData(typeof(DropsTheReport))]
    public void A_report_an_upcaster_fails_on_is_uninterpretable_and_never_fails_the_read(Type upcaster)
    {
        var chain = Chain("2", new EfSchemaUpcasterDescriptor(upcaster, "1", "2"));

        var read = ReadReport(Row("1", Report), chain);

        Assert.Null(read);
    }

    /// <summary>
    /// A report two versions behind is upcast before it is parsed: its version-1 shape names its section
    /// <c>legacyReadability</c>, which the strict current reader refuses, so only an upcast report reads at all.
    /// </summary>
    [Fact]
    public void A_report_two_versions_behind_is_upcast_before_it_is_read()
    {
        var chain = Chain("3", Step<RenameSection>("1", "2"), Step<Unchanged>("2", "3"));
        var legacy = Report.Replace("\"readability\"", "\"legacyReadability\"", StringComparison.Ordinal);

        var read = ReadReport(Row("1", legacy), chain);

        Assert.Equal("F", Assert.Single(read!.Readability!.Entries).Family);
        Assert.Null(ReadReport(Row("3", legacy), chain));
    }

    private static ClusterMemberEntity Row(string stamp, string report) => new() { SchemaVersion = stamp, ReportJson = report };

    /// <summary>The family's own declaration, content columns included, at <paramref name="current"/> over <paramref name="steps"/>.</summary>
    private static EfSchemaChain Chain(string current, params EfSchemaUpcasterDescriptor[] steps) =>
        EfSchemaChain.For(EfSchemaFamilyCatalog.Discover([typeof(ClusterMembershipEfModule).Assembly])
            .Single(family => family.Name == ClusterMembershipEfModule.SchemaFamily) with
        {
            CurrentVersion = current,
            Upcasters = steps
        });

    private static EfSchemaUpcasterDescriptor Step<TUpcaster>(string from, string to) where TUpcaster : IEfSchemaUpcaster => new(typeof(TUpcaster), from, to);

    /// <summary>The store's report read, over <paramref name="chain"/>.</summary>
    private static MemberReport? ReadReport(ClusterMemberEntity row, EfSchemaChain chain) =>
        StoredMember.ReadReport(row, chain);

    private sealed class Failing : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => throw new FormatException("The report is not in this version's shape.");
    }

    private sealed class DropsTheReport : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => new(row.Entity);
    }

    private sealed class RenameSection : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            var report = JsonNode.Parse(row[nameof(ClusterMemberEntity.ReportJson)]!)!.AsObject();
            var section = report["legacyReadability"];
            report.Remove("legacyReadability");
            report["readability"] = section;
            return row.With(nameof(ClusterMemberEntity.ReportJson), report.ToJsonString());
        }
    }

    private sealed class Unchanged : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }
}
