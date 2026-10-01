using System.Reflection;
using System.Text.Json.Nodes;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The EF provider's store beyond what the provider-neutral kit can reach: entries another provider version or a damaged
/// store wrote, failures at the store boundary, and the heartbeat's republish and cache refresh.
/// </summary>
public sealed class EfClusterMembershipStoreTests : IAsyncDisposable
{
    private const string Family = "store-family";
    private readonly EfClusterMembershipConformanceFixture _fixture = new(EfClusterMembershipTestStore.CreateSqlite());

    private ConformanceTimings Timings => _fixture.Timings;

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task An_entry_with_a_status_this_build_does_not_know_is_returned_with_an_unknown_report_and_counted()
    {
        var reader = await StartAsync("reader");
        var newer = NewHostId("newer");
        await _fixture.WriteRowAsync(_fixture.NewRow(newer) with { Status = "Suspended" });

        var seen = Assert.Single((await FreshAsync(reader)).Members, member => member.HostId == newer);

        Assert.True(seen.Report.IsUnknown);
        Assert.True(seen is { IsLive: true, Status: MemberStatus.Active });
        Assert.Contains(seen.Conditions, condition => condition.Kind == MemberConditionKind.UninterpretableEntry);
        Assert.Equal(newer, Assert.Single((await CountingAsync(reader)).Failures).Member.HostId);
    }

    /// <summary>
    /// A readability entry with a field this build does not know could narrow what the member reads; ignoring it would
    /// credit the member with more than it reported, so the whole report reads as unknown.
    /// </summary>
    [Fact]
    public Task A_report_with_a_field_this_build_does_not_know_is_returned_as_unknown_rather_than_read_in_part() =>
        AssertStoredReportReadsAsUnknownAsync(
            $$$"""{"readability":{"entries":[{"family":"{{{Family}}}","efModule":"M","readableVersions":["1"],"databaseIdentity":null,"observedFinalizedVersion":null,"moduleActive":true,"excludes":["1"]}]},"runnability":null}""");

    /// <summary>
    /// Every writer writes every entry field, so a document without one was not written by this envelope. Reading the
    /// missing field as its default would credit the member with a claim it never made, such as having observed no
    /// finalized version, so the report reads as unknown instead.
    /// </summary>
    [Theory]
    [InlineData("databaseIdentity")]
    [InlineData("observedFinalizedVersion")]
    [InlineData("moduleActive")]
    public Task A_report_missing_an_entry_field_is_returned_as_unknown_rather_than_read_with_a_default(string omitted)
    {
        var entry = new JsonObject
        {
            ["family"] = Family,
            ["efModule"] = "M",
            ["readableVersions"] = new JsonArray("1"),
            ["databaseIdentity"] = null,
            ["observedFinalizedVersion"] = null,
            ["moduleActive"] = true
        };
        Assert.True(entry.Remove(omitted), $"{omitted} is not a field of the entry this test builds.");

        return AssertStoredReportReadsAsUnknownAsync(
            new JsonObject { ["readability"] = new JsonObject { ["entries"] = new JsonArray(entry) }, ["runnability"] = null }.ToJsonString());
    }

    /// <summary>
    /// Runnability entries are held to the same strictness (spec 184, FR-008): a consumer with a field this build does not
    /// know, an entry missing a field, a null consumer, and a report without the runnability section at all were not
    /// written by this envelope, and reading any of them in part could place work on a member that never said it can run
    /// it.
    /// </summary>
    [Theory]
    [InlineData("""{"readability":null,"runnability":{"entries":[{"consumers":[{"consumerKey":"clr","schemaVersions":["1"],"since":"2"}],"storageDrivers":[],"activityTypes":[],"databaseIdentity":null}]}}""")]
    [InlineData("""{"readability":null,"runnability":{"entries":[{"consumers":[],"storageDrivers":[],"activityTypes":[]}]}}""")]
    [InlineData("""{"readability":null,"runnability":{"entries":[{"consumers":[null],"storageDrivers":[],"activityTypes":[],"databaseIdentity":null}]}}""")]
    [InlineData("""{"readability":null}""")]
    public Task A_runnability_section_this_build_cannot_read_whole_makes_the_report_unknown(string reportJson) =>
        AssertStoredReportReadsAsUnknownAsync(reportJson);

    /// <summary>
    /// The stored envelope names every field of a section's entries one by one, so a field an entry gains would be
    /// dropped on write and read back as its default: a report that looks whole but says less than the member published.
    /// An entry built with every constructor parameter set, all the way down, must come back with every public property
    /// equal.
    /// </summary>
    [Fact]
    public async Task Every_readability_entry_field_round_trips_through_the_stored_report()
    {
        var published = (ReadabilityEntry)Sample(typeof(ReadabilityEntry), "readability");
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", published);

        AssertEveryFieldRoundTripped(published, Assert.Single((await SeenAsync(reader, Identity(subject)))!.Report.Readability!.Entries));
    }

    /// <summary>The same for the runnability section, its entries and their consumers (spec 184, FR-008).</summary>
    [Fact]
    public async Task Every_runnability_section_and_entry_field_round_trips_through_the_stored_report()
    {
        var published = (RunnabilitySection)Sample(typeof(RunnabilitySection), "runnability");
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", runs: published.Entries);

        AssertEveryFieldRoundTripped(published, (await SeenAsync(reader, Identity(subject)))!.Report.Runnability!);
    }

    /// <summary>
    /// A populated runnability section, beside a readability section, reads back equal: several entries, one naming a
    /// database and one naming none, consumers at several schema versions, drivers and activity types.
    /// </summary>
    [Fact]
    public async Task A_populated_runnability_section_round_trips_beside_the_readability_section()
    {
        RunnabilityEntry[] runnability =
        [
            new([new RunnableConsumer("clr", ["1", "2"]), new RunnableConsumer("acme.approvals", ["3"])], ["json", "blob"], ["Acme.Approve", "Elsa.WriteLine"], "db-a"),
            new([new RunnableConsumer("clr", ["1"])], [], ["Elsa.WriteLine"])
        ];
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", Reads("1", "2"), runnability);

        var seen = (await SeenAsync(reader, Identity(subject)))!;

        Assert.False(seen.Report.IsUnknown);
        Assert.Equal(new MemberReport(new ReadabilitySection([Reads("1", "2")]), new RunnabilitySection(runnability)), seen.Report);
    }

    /// <summary>
    /// A family shared by no single EF module, as the finalization tables' is, has a null module (spec 180, FR-001). It
    /// must round-trip as null: refusing it would fail every publish of a host that has loaded one, and reading it as
    /// unknown would stop that host counting for every family.
    /// </summary>
    [Fact]
    public async Task A_shared_family_with_no_module_round_trips_with_its_module_null()
    {
        var shared = new ReadabilityEntry(Family, efModule: null, ["1"]);
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", shared);

        var seen = (await SeenAsync(reader, Identity(subject)))!;

        Assert.False(seen.Report.IsUnknown);
        Assert.Null(Assert.Single(seen.Report.Readability!.Entries).EfModule);
        Assert.Equal(new MemberReport(new ReadabilitySection([shared])), seen.Report);
    }

    [Fact]
    public async Task An_uninterpretable_entry_that_claims_it_left_is_still_counted_until_its_own_expiry_passes()
    {
        var reader = await StartAsync("reader");
        var newer = NewHostId("newer");
        var row = _fixture.NewRow(newer) with { Status = "Left", LeftAtUtcTicks = _fixture.Clock.GetUtcNow().UtcTicks, SchemaVersion = "2.0.0" };
        await _fixture.WriteRowAsync(row);

        Assert.Equal(newer, Assert.Single((await CountingAsync(reader)).Failures).Member.HostId);

        await _fixture.AdvanceAsync(Timings.ExpiryPeriod + Timings.SkewAllowance + TimeSpan.FromMilliseconds(1));
        Assert.True((await CountingAsync(reader)).EveryConsideredMemberMatches);
    }

    /// <summary>A row nothing can be said about fails the read loudly; leaving it out would be a partial view (FR-012).</summary>
    [Fact]
    public async Task A_row_whose_identity_cannot_be_formed_fails_the_fresh_read_instead_of_being_left_out()
    {
        var reader = await StartAsync("reader");
        await _fixture.WriteRowAsync(_fixture.NewRow("   "));

        var failure = await Assert.ThrowsAsync<ClusterMembershipReadException>(async () => await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh));
        Assert.IsType<ArgumentException>(failure.InnerException, exactMatch: false);
    }

    /// <summary>FR-034: a provider failure leaves the store wrapped, and nothing it says names the connection.</summary>
    [Fact]
    public async Task A_provider_failure_is_wrapped_at_the_store_boundary_and_names_no_connection()
    {
        const string unreachable = "/elsa-cluster-membership-no-such-directory/members.db";
        var services = new ServiceCollection()
            .Configure<ClusterMembershipOptions>(options => options.HostId = NewHostId("unreachable"))
            .AddEfClusterMembership(new EfClusterMembershipOptions { Provider = "Sqlite", ConnectionString = $"Data Source={unreachable};Mode=ReadWrite" });
        await using var provider = services.BuildServiceProvider();
        var membership = (EfClusterMembership)provider.GetRequiredService<IClusterMembership>();

        var join = await Assert.ThrowsAsync<ClusterMembershipStoreException>(() => membership.JoinAsync());
        var read = await Assert.ThrowsAsync<ClusterMembershipReadException>(async () => await membership.ReadFleetAsync(FleetReadMode.Fresh));

        foreach (var failure in new Exception[] { join, read })
            Assert.DoesNotContain(unreachable, failure.Message, StringComparison.Ordinal);
        Assert.NotNull(join.InnerException);
        Assert.IsType<ClusterMembershipStoreException>(read.InnerException);
    }

    [Fact]
    public async Task A_member_whose_entry_was_displaced_cannot_publish_and_knows_it_was_displaced()
    {
        var member = (EfClusterMembershipConformanceFixture.Member)await StartAsync("cut-off");
        await member.IsolateAsync();
        await _fixture.AdvanceAsync(Timings.ExpiryPeriod + Timings.SkewAllowance + Timings.HeartbeatInterval);
        await _fixture.StartMemberAsync(new ConformanceMemberSetup(Identity(member).HostId, []));
        member.Reconnect();

        await Assert.ThrowsAsync<ClusterMembershipStoreException>(async () => await member.Membership.PublishReportAsync());

        Assert.Equal(MemberLapseReason.Displaced, member.Membership.GetLocalStanding().Lapse?.Reason);
    }

    /// <summary>FR-027: every heartbeat republishes a changed report, so a change reaches the fleet without a publish.</summary>
    [Fact]
    public async Task A_heartbeat_republishes_a_changed_report()
    {
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", Reads("1"));
        var before = (await SeenAsync(reader, Identity(subject)))!.ReportRevision;

        subject.SetReadability(Reads("1", "2"));
        await _fixture.AdvanceAsync(Timings.HeartbeatInterval);

        var seen = (await SeenAsync(reader, Identity(subject)))!;
        Assert.Equal(new MemberReport(new ReadabilitySection([Reads("1", "2")])), seen.Report);
        Assert.True(seen.ReportRevision > before);
    }

    /// <summary>FR-010: a cached read may lag, by at most one heartbeat interval: the reader's next heartbeat refreshes it.</summary>
    [Fact]
    public async Task A_cached_read_lags_by_at_most_one_heartbeat_interval()
    {
        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", Reads("1"));
        await reader.Membership.ReadFleetAsync(FleetReadMode.Cached);
        subject.SetReadability(Reads("1", "2"));
        var published = await subject.Membership.PublishReportAsync();

        await _fixture.AdvanceAsync(Timings.HeartbeatInterval);
        var cached = await reader.Membership.ReadFleetAsync(FleetReadMode.Cached);

        Assert.Equal(FleetReadMode.Cached, cached.ReadMode);
        Assert.True(cached.Find(published.Member)!.ReportRevision >= published.Revision);
    }

    private static string NewHostId(string name) => $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)];

    private static ReadabilityEntry Reads(params string[] versions) => new(Family, "StoreModule", versions);

    /// <summary>
    /// A distinct, non-default value of <paramref name="type"/>: a string, a sequence of samples, or a report model built
    /// through its one public constructor with every parameter sampled, all the way down. Every parameter must surface as
    /// a property of the same name, which is what <see cref="AssertEveryFieldRoundTripped"/> compares.
    /// </summary>
    private static object Sample(Type type, string name)
    {
        if (type == typeof(string))
            return $"{name}-sample";
        // The opposite of what an entry built without saying reads as, so a field left unmapped reads back differently.
        if (type == typeof(bool))
            return false;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GenericTypeArguments[0];
            var samples = Array.CreateInstance(element, 2);
            for (var index = 0; index < samples.Length; index++)
                samples.SetValue(Sample(element, $"{name}-{index + 1}"), index);
            return samples;
        }

        if (type.Namespace != typeof(MemberReport).Namespace)
            throw new InvalidOperationException(
                $"A report model gained parameter '{name}' of type {type}. Give it a sample here, and map it in MemberReportJson.");

        var constructor = Assert.Single(type.GetConstructors());
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.All(constructor.GetParameters(), parameter =>
            Assert.Contains(properties, property => string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)));
        return constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType, parameter.Name!)).ToArray());
    }

    /// <summary>Every public property of <paramref name="read"/> equals <paramref name="published"/>'s, recursing into report
    /// models and sequences, and each was set in the sample, so the comparison proves that the field round-trips.</summary>
    private static void AssertEveryFieldRoundTripped(object published, object read)
    {
        foreach (var property in published.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var field = $"{published.GetType().Name}.{property.Name}";
            var expected = property.GetValue(published);
            var actual = property.GetValue(read);
            Assert.False(expected is null || expected is IEnumerable<object> values && !values.Any(), $"{field} is not set in the sample, so this test cannot prove it round-trips.");
            switch (expected)
            {
                case string or bool:
                    Assert.Equal(expected, actual);
                    break;
                case IEnumerable<object> sequence:
                    var expectedItems = sequence.ToArray();
                    var actualItems = Assert.IsAssignableFrom<IEnumerable<object>>(actual).ToArray();
                    Assert.Equal(expectedItems.Length, actualItems.Length);
                    foreach (var (expectedItem, actualItem) in expectedItems.Zip(actualItems))
                    {
                        if (expectedItem is string)
                            Assert.Equal(expectedItem, actualItem);
                        else
                            AssertEveryFieldRoundTripped(expectedItem, actualItem);
                    }

                    break;
                default:
                    Assert.Fail($"{field} is of type {property.PropertyType}, which this test does not compare. Teach it to, and map the field in MemberReportJson.");
                    break;
            }
        }
    }

    /// <summary>A row holding <paramref name="reportJson"/> is returned with an unknown report and counted as a failure.</summary>
    private async Task AssertStoredReportReadsAsUnknownAsync(string reportJson)
    {
        var reader = await StartAsync("reader");
        var writer = NewHostId("writer");
        await _fixture.WriteRowAsync(_fixture.NewRow(writer) with { ReportJson = reportJson });

        var seen = Assert.Single((await FreshAsync(reader)).Members, member => member.HostId == writer);

        Assert.True(seen.Report.IsUnknown);
        Assert.Equal(writer, Assert.Single((await CountingAsync(reader)).Failures).Member.HostId);
    }

    private static ClusterMemberIdentity Identity(IConformanceMember member) => member.Membership.GetLocalStanding().Identity;

    private async Task<IConformanceMember> StartAsync(string name, ReadabilityEntry? reads = null, IReadOnlyList<RunnabilityEntry>? runs = null) =>
        await _fixture.StartMemberAsync(new ConformanceMemberSetup(NewHostId(name), reads is null ? [] : [reads], Runnability: runs));

    private static async Task<FleetView> FreshAsync(IConformanceMember reader) => await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh);

    private static async Task<FleetMember?> SeenAsync(IConformanceMember reader, ClusterMemberIdentity identity) => (await FreshAsync(reader)).Find(identity);

    private static async Task<MemberQueryAnswer> CountingAsync(IConformanceMember reader) =>
        await reader.Membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, "1")), FleetReadMode.Fresh);
}
