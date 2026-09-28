using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Tests.Core;

/// <summary>
/// The runnability requirement kinds and section of spec 184 (FR-006, FR-008, FR-009): a member matches when one of its
/// entries that applies to the queried database satisfies every runnability requirement.
/// </summary>
public sealed class RunnabilityQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly ActivatesRuntimeConsumer ActivatesClrOne = new("clr", "1");
    private static readonly ResolvesActivityType ResolvesApprove = new("Acme.Approve");
    private static readonly HasStorageDriver HasJson = new("json");

    [Fact]
    public void A_placement_query_matches_only_members_whose_entry_satisfies_every_requirement()
    {
        var upgraded = Member("upgraded", Entry(activityTypes: ["Acme.Approve", "Elsa.WriteLine"]));
        var old = Member("old", Entry(activityTypes: ["Elsa.WriteLine"]));

        var answer = Evaluate(MemberQuery.Placement(ActivatesClrOne, HasJson, ResolvesApprove), upgraded, old);

        Assert.Equal(new[] { upgraded }, answer.Matches);
        var failure = Assert.Single(answer.Failures);
        Assert.Same(old, failure.Member);
        Assert.Same(ResolvesApprove, failure.Requirement);
    }

    [Fact]
    public void Two_entries_that_each_satisfy_part_of_the_requirement_do_not_add_up()
    {
        var split = Member("split", Entry(activityTypes: ["Acme.Approve"], drivers: []), Entry(activityTypes: [], drivers: ["json"]));

        var failure = Assert.Single(Evaluate(MemberQuery.Placement(ResolvesApprove, HasJson), split).Failures);

        Assert.Same(HasJson, failure.Requirement);
        Assert.Equal(new[] { split }, Evaluate(MemberQuery.Placement(ResolvesApprove), split).Matches);
        Assert.Equal(new[] { split }, Evaluate(MemberQuery.Placement(HasJson), split).Matches);
    }

    [Fact]
    public void A_consumer_matches_only_at_a_schema_version_it_activates()
    {
        var member = Member("member", Entry(consumers: [new RunnableConsumer("clr", ["1", "2"])]));

        Assert.Single(Evaluate(MemberQuery.Placement(new ActivatesRuntimeConsumer("clr", "2")), member).Matches);
        Assert.Single(Evaluate(MemberQuery.Placement(new ActivatesRuntimeConsumer("clr", "3")), member).Failures);
        Assert.Single(Evaluate(MemberQuery.Placement(new ActivatesRuntimeConsumer("graph", "1")), member).Failures);
    }

    [Fact]
    public void An_entry_applies_to_the_database_it_names_or_to_every_database_when_it_names_none()
    {
        var namesIt = Member("names-it", Entry(databaseIdentity: "db-a"));
        var namesNone = Member("names-none", Entry());
        var namesAnother = Member("names-another", Entry(databaseIdentity: "db-b"));

        var answer = Evaluate(MemberQuery.Placement(new ResolvesActivityType("Acme.Approve", "db-a")), namesIt, namesNone, namesAnother);

        Assert.Equal(new[] { namesIt, namesNone }, answer.Matches);
        Assert.Same(namesAnother, Assert.Single(answer.Failures).Member);
    }

    [Fact]
    public void A_member_without_a_runnability_section_meets_no_runnability_requirement_for_either_purpose()
    {
        var silent = Member("silent", report: new MemberReport(new ReadabilitySection([])));

        Assert.Same(silent, Assert.Single(Evaluate(MemberQuery.Placement(ResolvesApprove), silent).Failures).Member);
        Assert.Same(silent, Assert.Single(Evaluate(MemberQuery.Counting(ResolvesApprove), silent).Failures).Member);
    }

    [Fact]
    public void An_unknown_report_is_never_placed_and_fails_a_counting_query()
    {
        var unknown = Member("unknown", report: MemberReport.Unknown);

        var placement = Evaluate(MemberQuery.Placement(ResolvesApprove), unknown);
        Assert.Empty(placement.Matches);
        Assert.Empty(placement.Failures);
        Assert.Same(unknown, Assert.Single(Evaluate(MemberQuery.Counting(ResolvesApprove), unknown).Failures).Member);
    }

    [Fact]
    public void An_empty_entry_satisfies_nothing()
    {
        var empty = Member("empty", new RunnabilityEntry([], [], []));

        Assert.Same(ActivatesClrOne, Assert.Single(Evaluate(MemberQuery.Placement(ActivatesClrOne, ResolvesApprove), empty).Failures).Requirement);
    }

    [Fact]
    public void Readability_and_runnability_requirements_are_reported_in_query_order()
    {
        var member = Member("member", Entry(activityTypes: []));
        var readsOne = new ReadsSchemaVersion("orders", "1");

        var failure = Assert.Single(Evaluate(MemberQuery.Placement(ResolvesApprove, readsOne), member).Failures);

        Assert.Same(ResolvesApprove, failure.Requirement);
    }

    [Fact]
    public void Equal_registries_publish_equal_entries_whatever_order_they_enumerate_in()
    {
        var first = new RunnabilityEntry(
            [new RunnableConsumer("graph", ["1"]), new RunnableConsumer("clr", ["2", "1"])],
            ["json", "blob"],
            ["B", "A", "A"]);
        var second = new RunnabilityEntry(
            [new RunnableConsumer("clr", ["1"]), new RunnableConsumer("clr", ["2"]), new RunnableConsumer("graph", ["1"])],
            ["blob", "json"],
            ["A", "B"]);

        Assert.Equal(first, second);
        Assert.Equal(new RunnabilitySection([first]), new RunnabilitySection([second]));
        Assert.Equal(new MemberReport(runnability: new RunnabilitySection([first])), new MemberReport(runnability: new RunnabilitySection([second])));
        Assert.NotEqual(first, new RunnabilityEntry([], ["json", "blob"], ["A", "B"]));
    }

    private static MemberQueryAnswer Evaluate(MemberQuery query, params FleetMember[] members) =>
        query.Evaluate(new FleetView(ClusterProviderKind.Durable, FleetReadMode.Cached, Now, members));

    private static FleetMember Member(string hostId, params RunnabilityEntry[] entries) =>
        Member(hostId, report: new MemberReport(runnability: new RunnabilitySection(entries)));

    private static FleetMember Member(string hostId, MemberReport report) =>
        new(
            new ClusterMemberIdentity(hostId, MemberIncarnation.New()),
            MemberStatus.Active,
            Now,
            TimeSpan.FromSeconds(30),
            IsLive: true,
            IsDisplaced: false,
            report,
            ReportRevision: 1,
            Conditions: []);

    private static RunnabilityEntry Entry(
        RunnableConsumer[]? consumers = null,
        string[]? drivers = null,
        string[]? activityTypes = null,
        string? databaseIdentity = null) =>
        new(
            consumers ?? [new RunnableConsumer("clr", ["1"])],
            drivers ?? ["json"],
            activityTypes ?? ["Acme.Approve"],
            databaseIdentity);
}
