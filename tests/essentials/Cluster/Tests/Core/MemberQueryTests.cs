using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Tests.Core;

public sealed class MemberQueryTests
{
    private const string Family = "orders";
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReadsSchemaVersion ReadsTwo = new(Family, "2");

    [Fact]
    public void Counting_considers_every_live_member_whatever_its_status_or_displacement()
    {
        var joining = Member("joining", MemberStatus.Joining);
        var active = Member("active");
        var draining = Member("draining", MemberStatus.Draining);
        var displaced = Member("displaced", displaced: true);
        var left = Member("left", MemberStatus.Left, live: false);
        var expired = Member("expired", live: false);

        var answer = Evaluate(MemberQuery.Counting(ReadsTwo), joining, active, draining, displaced, left, expired);

        Assert.Equal(new[] { joining, active, draining, displaced }, answer.Matches);
        Assert.Empty(answer.Failures);
    }

    [Fact]
    public void Placement_considers_only_the_current_active_incarnation_with_a_known_report()
    {
        var active = Member("active");
        var joining = Member("joining", MemberStatus.Joining);
        var draining = Member("draining", MemberStatus.Draining);
        var displaced = Member("displaced", displaced: true);
        var expired = Member("expired", live: false);
        var unknown = Member("unknown", report: MemberReport.Unknown);

        var answer = Evaluate(MemberQuery.Placement(ReadsTwo), active, joining, draining, displaced, expired, unknown);

        Assert.Equal(new[] { active }, answer.Matches);
        Assert.Empty(answer.Failures);
    }

    [Fact]
    public void Placement_drops_a_host_id_with_two_undisplaced_live_incarnations()
    {
        var first = Member("doubtful");
        var second = Member("doubtful");
        var other = Member("other");

        Assert.Equal(new[] { other }, Evaluate(MemberQuery.Placement(ReadsTwo), first, second, other).Matches);
    }

    [Fact]
    public void An_unknown_report_fails_every_counting_requirement()
    {
        var unknown = Member("unknown", report: MemberReport.Unknown);

        var failure = Assert.Single(Evaluate(MemberQuery.Counting(new ReadsSchemaVersion("elsewhere", "1")), unknown).Failures);

        Assert.Same(unknown, failure.Member);
    }

    [Fact]
    public void A_member_without_a_readability_section_cannot_say_what_it_reads_so_it_fails_for_either_purpose()
    {
        var silent = Member("silent", report: MemberReport.Empty);

        Assert.Same(silent, Assert.Single(Evaluate(MemberQuery.Counting(ReadsTwo), silent).Failures).Member);
        Assert.Same(silent, Assert.Single(Evaluate(MemberQuery.Placement(ReadsTwo), silent).Failures).Member);
    }

    [Fact]
    public void A_member_that_does_not_declare_the_family_is_not_counted_and_cannot_be_placed()
    {
        var unrelated = Member("unrelated", report: Reports(Entry("customers", ["1"])));

        Assert.Equal(new[] { unrelated }, Evaluate(MemberQuery.Counting(ReadsTwo), unrelated).Matches);
        Assert.Same(unrelated, Assert.Single(Evaluate(MemberQuery.Placement(ReadsTwo), unrelated).Failures).Member);
    }

    [Fact]
    public void A_member_that_declares_the_family_blocks_counting_until_it_reads_the_version()
    {
        var old = Member("old", report: Reports(Entry(Family, ["1"])));
        var upgraded = Member("upgraded", report: Reports(Entry(Family, ["1", "2"])));

        var answer = Evaluate(MemberQuery.Counting(ReadsTwo), old, upgraded);

        Assert.False(answer.EveryConsideredMemberMatches);
        Assert.Equal(new[] { upgraded }, answer.Matches);
        var blocker = Assert.Single(answer.Failures);
        Assert.Same(old, blocker.Member);
        Assert.Same(ReadsTwo, blocker.Requirement);
    }

    [Fact]
    public void Every_entry_that_speaks_for_the_database_must_read_the_version()
    {
        var narrowed = Member("narrowed", report: Reports(Entry(Family, ["1", "2"]), Entry(Family, ["1"])));

        Assert.Same(narrowed, Assert.Single(Evaluate(MemberQuery.Counting(ReadsTwo), narrowed).Failures).Member);
        Assert.Same(narrowed, Assert.Single(Evaluate(MemberQuery.Placement(ReadsTwo), narrowed).Failures).Member);
    }

    [Fact]
    public void A_database_identity_counts_members_that_name_it_or_name_none()
    {
        var namesIt = Member("names-it", report: Reports(Entry(Family, ["1"], "db-a")));
        var namesNone = Member("names-none", report: Reports(Entry(Family, ["1"])));
        var namesAnother = Member("names-another", report: Reports(Entry(Family, ["1"], "db-b")));

        var answer = Evaluate(MemberQuery.Counting(new ReadsSchemaVersion(Family, "2", "db-a")), namesIt, namesNone, namesAnother);

        Assert.Equal(new[] { namesAnother }, answer.Matches);
        Assert.Equal(new[] { namesIt, namesNone }, answer.Failures.Select(failure => failure.Member));
    }

    [Fact]
    public void Without_a_database_identity_every_entry_for_the_family_counts()
    {
        var namesAnother = Member("names-another", report: Reports(Entry(Family, ["1"], "db-b")));

        Assert.Same(namesAnother, Assert.Single(Evaluate(MemberQuery.Counting(ReadsTwo), namesAnother).Failures).Member);
    }

    [Fact]
    public void A_failure_names_the_first_requirement_the_member_failed()
    {
        var member = Member("member", report: Reports(Entry(Family, ["1"])));
        var readsOne = new ReadsSchemaVersion(Family, "1");
        var readsThree = new ReadsSchemaVersion(Family, "3");

        var failure = Assert.Single(Evaluate(MemberQuery.Placement(readsOne, ReadsTwo, readsThree), member).Failures);

        Assert.Same(ReadsTwo, failure.Requirement);
    }

    [Fact]
    public void A_query_without_requirements_matches_every_member_it_considers()
    {
        var active = Member("active");
        var unknown = Member("unknown", report: MemberReport.Unknown);

        Assert.Equal(new[] { active, unknown }, Evaluate(MemberQuery.Counting(), active, unknown).Matches);
    }

    /// <summary>
    /// Spec 186, FR-012 and MR-001, both ways: the backfill's settle condition holds only once every counted member that
    /// speaks for the database reports an observed finalized version among the target and later; a member that observed
    /// an older one, or none at all, or whose report is unknown, holds it back, and one that does not declare the family
    /// or speaks for another database does not.
    /// </summary>
    [Fact]
    public void The_settle_condition_holds_only_once_every_counted_member_reports_observing_the_target_or_later()
    {
        var settled = new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a");
        var atTarget = Member("at-target", report: Reports(Observing(Family, "2", "db-a")));
        var later = Member("later", report: Reports(Observing(Family, "3", null)));
        var behind = Member("behind", report: Reports(Observing(Family, "1", "db-a")));
        var nothing = Member("nothing", report: Reports(Entry(Family, ["1", "2", "3"])));
        var unknown = Member("unknown", report: MemberReport.Unknown);
        var unrelated = Member("unrelated", report: Reports(Observing("customers", "1", null)));
        var elsewhere = Member("elsewhere", report: Reports(Observing(Family, "1", "db-b")));
        var expired = Member("expired", live: false, report: Reports(Observing(Family, "1", "db-a")));

        var answer = Evaluate(MemberQuery.Counting(settled), atTarget, later, behind, nothing, unknown, unrelated, elsewhere, expired);

        Assert.Equal(new[] { atTarget, later, unrelated, elsewhere }, answer.Matches);
        Assert.Equal(new[] { behind, nothing, unknown }, answer.Failures.Select(failure => failure.Member));
        Assert.True(Evaluate(MemberQuery.Counting(settled), atTarget, later, unrelated, elsewhere, expired).EveryConsideredMemberMatches);
    }

    [Fact]
    public void A_settle_condition_names_at_least_one_version_and_compares_by_value()
    {
        Assert.Throws<ArgumentException>(() => new ObservesFinalizedSchemaVersion(Family, []));
        Assert.Equal(new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a"), new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a"));
        Assert.NotEqual(new ObservesFinalizedSchemaVersion(Family, ["2"], "db-a"), new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a"));
    }

    /// <summary>Equal settle conditions hash alike, and ones that differ only in a version do not collide by design.</summary>
    [Fact]
    public void A_settle_condition_hashes_every_version_it_names()
    {
        Assert.Equal(
            new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a").GetHashCode(),
            new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a").GetHashCode());
        Assert.NotEqual(
            new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a").GetHashCode(),
            new ObservesFinalizedSchemaVersion(Family, ["2", "4"], "db-a").GetHashCode());
        Assert.Equal(2, new HashSet<MemberRequirement>
        {
            new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a"),
            new ObservesFinalizedSchemaVersion(Family, ["2", "3"], "db-a"),
            new ObservesFinalizedSchemaVersion(Family, ["2", "4"], "db-a")
        }.Count);
    }

    private static MemberQueryAnswer Evaluate(MemberQuery query, params FleetMember[] members) =>
        query.Evaluate(new FleetView(ClusterProviderKind.Durable, FleetReadMode.Fresh, Now, members));

    private static FleetMember Member(
        string hostId,
        MemberStatus status = MemberStatus.Active,
        bool live = true,
        bool displaced = false,
        MemberReport? report = null) =>
        new(
            new ClusterMemberIdentity(hostId, MemberIncarnation.New()),
            status,
            Now,
            TimeSpan.FromSeconds(30),
            live,
            displaced,
            report ?? Reports(Entry(Family, ["1", "2"])),
            ReportRevision: 1,
            Conditions: []);

    private static ReadabilityEntry Entry(string family, string[] versions, string? databaseIdentity = null) =>
        new(family, "OrdersModule", versions, databaseIdentity);

    private static ReadabilityEntry Observing(string family, string observed, string? databaseIdentity) =>
        new(family, "OrdersModule", ["1", "2", "3"], databaseIdentity, observed);

    private static MemberReport Reports(params ReadabilityEntry[] entries) => new(new ReadabilitySection(entries));
}
