using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Cluster.Testing;

public abstract partial class ClusterMembershipConformanceTests
{
    private bool IsDefaultProvider => Fixture.ProviderKind == ClusterProviderKind.InProcess;

    [SkippableFact]
    public async Task FR056_two_opt_in_providers_in_one_host_fail_at_startup_naming_both()
    {
        Skip.If(IsDefaultProvider, "The in-process provider is the default, not an opt-in provider.");

        foreach (var compositions in BothOrders(Fixture.ComposeProvider, ConformanceSentinelProvider.Compose))
        {
            var failure = await StartupFailureAsync(NewHostId("host"), compositions);

            Assert.True(failure is not null, "A host composing two opt-in providers started: one of them won by last-write-wins.");
            Assert.Contains(Fixture.ProviderName, failure, StringComparison.Ordinal);
            Assert.Contains(ConformanceSentinelProvider.Name, failure, StringComparison.Ordinal);
        }
    }

    [SkippableFact]
    public async Task FR056_the_in_process_default_yields_to_one_opt_in_provider_in_either_order()
    {
        Action<IServiceCollection> composeDefault = IsDefaultProvider ? Fixture.ComposeProvider : ConformanceSentinelProvider.ComposeDefault;
        Action<IServiceCollection> composeOptIn = IsDefaultProvider ? ConformanceSentinelProvider.Compose : Fixture.ComposeProvider;
        var optIn = IsDefaultProvider ? ConformanceSentinelProvider.Name : Fixture.ProviderName;

        foreach (var compositions in BothOrders(composeDefault, composeOptIn))
        {
            using var host = await StartHostAsync(NewHostId("host"), compositions);

            Assert.Equal(optIn, Assert.Single(host.Services.GetServices<ClusterMembershipProviderRegistration>()).Name);
            Assert.Single(host.Registrations, descriptor => descriptor.ServiceType == typeof(IClusterMembership));
        }
    }

    /// <remarks>
    /// A membership registered after the provider would otherwise win by last-write-wins. Registered before a durable
    /// provider, it is refused at composition; registered before the in-process default, the default yields to it,
    /// because the default only ever fills an empty slot.
    /// </remarks>
    [SkippableFact]
    public async Task FR001_a_membership_registered_directly_beside_the_provider_fails_at_startup_naming_both()
    {
        var orders = BothOrders(Fixture.ComposeProvider, ConformanceSentinelProvider.ComposeDirectly).Take(IsDefaultProvider ? 1 : 2);

        foreach (var compositions in orders)
        {
            var failure = await StartupFailureAsync(NewHostId("host"), compositions);

            Assert.True(failure is not null, "A host with a membership registered beside its provider started.");
            Assert.Contains(Fixture.ProviderName, failure, StringComparison.Ordinal);
            Assert.Contains(nameof(ConformanceSentinelProvider.InertMembership), failure, StringComparison.Ordinal);
        }
    }

    [SkippableFact]
    public async Task FR003a_a_durable_provider_without_an_explicit_host_id_refuses_at_startup_before_joining()
    {
        Skip.If(IsDefaultProvider, "The in-process provider takes the machine name as its host id by default.");
        var stored = await Fixture.CountStoredEntriesAsync();

        var failure = await StartupFailureAsync(hostId: null, Fixture.ComposeProvider);

        Assert.True(failure is not null, "A durable provider without an explicit host id started.");
        Assert.Contains(Fixture.ProviderName, failure, StringComparison.Ordinal);
        Assert.Contains("HostId", failure, StringComparison.Ordinal);
        Assert.Equal(stored, await Fixture.CountStoredEntriesAsync());

        using (await StartHostAsync(NewHostId("explicit"), Fixture.ComposeProvider))
            Assert.Equal(stored + 1, await Fixture.CountStoredEntriesAsync());
    }

    [SkippableFact]
    public async Task FR017_every_container_built_from_the_host_registrations_sees_the_same_member()
    {
        using var host = await StartHostAsync(NewHostId("shared"), Fixture.ComposeProvider);
        await using var shell = host.BuildShellContainer();
        var root = host.Services.GetRequiredService<IClusterMembership>();
        var copy = shell.GetRequiredService<IClusterMembership>();

        Assert.Equal(root.GetLocalStanding().Identity, copy.GetLocalStanding().Identity);

        var published = await copy.PublishReportAsync();
        var rootView = await root.ReadFleetAsync(FleetReadMode.Fresh);
        var seen = rootView.Find(published.Member);
        Assert.NotNull(seen);
        Assert.True(seen.ReportRevision >= published.Revision);
        AssertIdentities(rootView.Members.Select(member => member.Identity), (await copy.ReadFleetAsync(FleetReadMode.Fresh)).Members);
    }

    [SkippableFact]
    public async Task FR058_a_member_query_returns_only_members_whose_report_satisfies_it()
    {
        RequireMultipleMembers();
        var active = await Fixture.StartMemberAsync(Setup("active", Reads("1", "2")) with { ClockOffset = DisplacedButStillLiveObserverOffset });
        var draining = await Fixture.StartMemberAsync(Setup("draining", Reads("1", "2")));
        var displaced = await Fixture.StartMemberAsync(Setup("displaced", Reads("1", "2")));
        var expired = await Fixture.StartMemberAsync(Setup("expired", Reads("1", "2")));
        var departed = await Fixture.StartMemberAsync(Setup("departed", Reads("1", "2")));
        var silent = await Fixture.StartMemberAsync(new ConformanceMemberSetup(NewHostId("silent"), Readability: null));
        var joining = await Fixture.JoinMemberAsync(Setup("joining", Reads("1", "2")));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);

        await expired.KillAsync();
        await Fixture.AdvanceAsync(Timings.ExpiryPeriod + Timings.SkewAllowance + Timings.HeartbeatInterval);
        await KillAndAdvanceToDisplaceableAsync(active, displaced);
        var restarted = await Fixture.StartMemberAsync(Restart(displaced, Reads("1", "2")));
        await draining.BeginStopAsync();
        await departed.StopAsync();
        var unknownHost = NewHostId("newer");
        await Fixture.PlantUninterpretableEntryAsync(unknownHost);
        var requirement = new ReadsSchemaVersion(Family, "2");

        var placement = await AskAsync(active, MemberQuery.Placement(requirement));
        AssertIdentities([Identity(active), Identity(restarted)], placement.Matches);
        AssertIdentities([Identity(silent)], placement.Failures.Select(failure => failure.Member));

        var counting = await AskAsync(active, MemberQuery.Counting(requirement));
        AssertIdentities([Identity(active), Identity(restarted), Identity(draining), Identity(displaced), Identity(joining)], counting.Matches);
        Assert.Equal(
            new[] { Identity(silent).HostId, unknownHost }.Order(StringComparer.Ordinal),
            counting.Failures.Select(failure => failure.Member.HostId).Order(StringComparer.Ordinal));
        Assert.All(counting.Failures, failure => Assert.Equal(requirement, failure.Requirement));
    }

    [SkippableFact]
    public async Task FR058_a_member_matches_its_own_report_for_either_purpose()
    {
        var member = await Fixture.StartMemberAsync(Setup("self", Reads("1", "2")));
        var identity = Identity(member);

        foreach (var purpose in new[] { MemberQueryPurpose.Counting, MemberQueryPurpose.Placement })
        {
            var readable = await AskAsync(member, new MemberQuery(purpose, [new ReadsSchemaVersion(Family, "2")]));
            AssertIdentities([identity], readable.Matches);

            var unreadable = await AskAsync(member, new MemberQuery(purpose, [new ReadsSchemaVersion(Family, "3")]));
            AssertIdentities([identity], unreadable.Failures.Select(failure => failure.Member));
        }

        var otherFamily = new ReadsSchemaVersion("another-family", "1");
        AssertIdentities([identity], (await AskAsync(member, MemberQuery.Counting(otherFamily))).Matches);
        AssertIdentities([identity], (await AskAsync(member, MemberQuery.Placement(otherFamily))).Failures.Select(failure => failure.Member));
    }

    private static Action<IServiceCollection>[][] BothOrders(Action<IServiceCollection> first, Action<IServiceCollection> second) =>
        [[first, second], [second, first]];

    /// <summary>Every message in the startup failure's chain, or <see langword="null"/> when the host started.</summary>
    private static async Task<string?> StartupFailureAsync(string? hostId, params Action<IServiceCollection>[] compositions)
    {
        try
        {
            using var host = await StartHostAsync(hostId, compositions);
            return null;
        }
        catch (Exception exception)
        {
            var messages = new List<string>();
            for (var current = exception; current is not null; current = current.InnerException)
                messages.Add(current.Message);
            return string.Join(Environment.NewLine, messages);
        }
    }
}
