using System.Reflection;
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
            $$$"""{"readability":{"entries":[{"family":"{{{Family}}}","efModule":"M","readableVersions":["1"],"databaseIdentity":null,"observedFinalizedVersion":null,"excludes":["1"]}]}}""");

    /// <summary>
    /// Every writer writes every entry field, so a document without one was not written by this envelope. Reading the
    /// missing field as its default would credit the member with a claim it never made, such as having observed no
    /// finalized version, so the report reads as unknown instead.
    /// </summary>
    [Fact]
    public Task A_report_missing_an_entry_field_is_returned_as_unknown_rather_than_read_with_a_default() =>
        AssertStoredReportReadsAsUnknownAsync(
            $$$"""{"readability":{"entries":[{"family":"{{{Family}}}","efModule":"M","readableVersions":["1"],"databaseIdentity":null}]}}""");

    /// <summary>
    /// The stored envelope names <see cref="ReadabilityEntry"/>'s fields one by one, so a field the entry gains would be
    /// dropped on write and read back as its default: a report that looks whole but says less than the member published.
    /// An entry with every constructor parameter set must come back with every public property equal.
    /// </summary>
    [Fact]
    public async Task Every_readability_entry_field_round_trips_through_the_stored_report()
    {
        var constructor = Assert.Single(typeof(ReadabilityEntry).GetConstructors());
        var parameters = constructor.GetParameters();
        var properties = typeof(ReadabilityEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.All(parameters, parameter => Assert.Contains(properties, property => string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)));
        var published = (ReadabilityEntry)constructor.Invoke(parameters.Select(SampleArgument).ToArray());

        var reader = await StartAsync("reader");
        var subject = await StartAsync("subject", published);
        var read = Assert.Single((await SeenAsync(reader, Identity(subject)))!.Report.Readability!.Entries);

        Assert.All(properties, property =>
        {
            var expected = property.GetValue(published);
            var actual = property.GetValue(read);
            Assert.False(expected is null || expected is IEnumerable<string> values && !values.Any(), $"{nameof(ReadabilityEntry)}.{property.Name} is not set in the sample entry, so this test cannot prove it round-trips.");
            if (expected is IEnumerable<string> sequence)
                Assert.Equal(sequence, Assert.IsAssignableFrom<IEnumerable<string>>(actual));
            else
                Assert.Equal(expected, actual);
        });
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

    /// <summary>A distinct, non-default argument for each of <see cref="ReadabilityEntry"/>'s constructor parameters.</summary>
    private static object SampleArgument(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(string) ? $"{parameter.Name}-sample"
        : parameter.ParameterType == typeof(IEnumerable<string>) ? new[] { $"{parameter.Name}-1", $"{parameter.Name}-2" }
        : throw new InvalidOperationException(
            $"{nameof(ReadabilityEntry)} gained parameter '{parameter.Name}' of type {parameter.ParameterType}. Give it a sample here, and map it in MemberReportJson.");

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

    private async Task<IConformanceMember> StartAsync(string name, ReadabilityEntry? reads = null) =>
        await _fixture.StartMemberAsync(new ConformanceMemberSetup(NewHostId(name), reads is null ? [] : [reads]));

    private static async Task<FleetView> FreshAsync(IConformanceMember reader) => await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh);

    private static async Task<FleetMember?> SeenAsync(IConformanceMember reader, ClusterMemberIdentity identity) => (await FreshAsync(reader)).Find(identity);

    private static async Task<MemberQueryAnswer> CountingAsync(IConformanceMember reader) =>
        await reader.Membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, "1")), FleetReadMode.Fresh);
}
