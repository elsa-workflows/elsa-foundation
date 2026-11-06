using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// The owner-bounded audit query (#3440 T021, experimental): ownership comes from the session owner's
/// association, and filtering and ordering happen inside the owning reader before the limit.
/// </summary>
public sealed class AgentAuditOwnershipTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Foreign_owners_and_tenants_never_consume_the_callers_limit()
    {
        var store = new InMemoryAgentAuditStore();
        Assert.True(await store.AssociateAsync("mine", "actor-a", "tenant-1"));
        Assert.True(await store.AssociateAsync("other-actor", "actor-b", "tenant-1"));
        Assert.True(await store.AssociateAsync("other-tenant", "actor-a", "tenant-2"));
        await EmitAsync(store, "mine-old", "mine", T0);
        await EmitAsync(store, "mine-new", "mine", T0.AddMinutes(1));
        // Many newer records owned by others would fill a global limit before the caller's records.
        for (var i = 0; i < 5; i++)
        {
            await EmitAsync(store, $"actor-b-{i}", "other-actor", T0.AddMinutes(10 + i));
            await EmitAsync(store, $"tenant-2-{i}", "other-tenant", T0.AddMinutes(10 + i));
        }

        var one = await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 1));
        var all = await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 100));

        Assert.Equal(AgentAuditQueryOutcome.Succeeded, one.Outcome);
        Assert.Equal(["mine-new"], one.Events.Select(x => x.Id));
        Assert.Equal(["mine-new", "mine-old"], all.Events.Select(x => x.Id));
        Assert.Equal(Enumerable.Range(0, 5).Reverse().Select(i => $"tenant-2-{i}"),
            (await store.ListOwnedAsync(new("actor-a", "tenant-2", null, 100))).Events.Select(x => x.Id));
    }

    [Fact]
    public async Task A_limit_of_one_orders_ties_by_latest_append_after_filtering()
    {
        var store = new InMemoryAgentAuditStore();
        await store.AssociateAsync("mine", "actor-a", "tenant-1");
        await store.AssociateAsync("foreign", "actor-b", "tenant-1");
        await EmitAsync(store, "tie-first", "mine", T0);
        await EmitAsync(store, "foreign-tie", "foreign", T0);
        await EmitAsync(store, "tie-second", "mine", T0);
        await EmitAsync(store, "foreign-later-tie", "foreign", T0);
        await EmitAsync(store, "older", "mine", T0.AddMinutes(-1));

        var one = await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 1));
        var all = await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 3));

        Assert.Equal(["tie-second"], one.Events.Select(x => x.Id));
        Assert.Equal(["tie-second", "tie-first", "older"], all.Events.Select(x => x.Id));
    }

    [Fact]
    public async Task An_explicit_session_narrows_only_within_the_callers_ownership()
    {
        var store = new InMemoryAgentAuditStore();
        await store.AssociateAsync("first", "actor-a", "tenant-1");
        await store.AssociateAsync("second", "actor-a", "tenant-1");
        await store.AssociateAsync("foreign", "actor-b", "tenant-1");
        await EmitAsync(store, "first-1", "first", T0);
        await EmitAsync(store, "second-1", "second", T0.AddMinutes(1));
        await EmitAsync(store, "foreign-1", "foreign", T0.AddMinutes(2));

        Assert.Equal(["first-1"], (await store.ListOwnedAsync(new("actor-a", "tenant-1", "first", 10))).Events.Select(x => x.Id));
        Assert.Empty((await store.ListOwnedAsync(new("actor-a", "tenant-1", "foreign", 10))).Events);
        Assert.Empty((await store.ListOwnedAsync(new("actor-a", "tenant-2", "first", 10))).Events);
    }

    [Fact]
    public async Task The_first_association_is_authoritative_and_a_conflicting_one_is_refused()
    {
        var store = new InMemoryAgentAuditStore();
        Assert.True(await store.AssociateAsync("session", "actor-a", "tenant-1"));
        Assert.True(await store.AssociateAsync("session", "actor-a", "tenant-1"));
        Assert.False(await store.AssociateAsync("session", "actor-b", "tenant-1"));
        Assert.False(await store.AssociateAsync("session", "actor-a", "tenant-2"));
        await EmitAsync(store, "event", "session", T0);

        Assert.Equal(["event"], (await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 10))).Events.Select(x => x.Id));
        Assert.Empty((await store.ListOwnedAsync(new("actor-b", "tenant-1", null, 10))).Events);
        Assert.Empty((await store.ListOwnedAsync(new("actor-a", "tenant-2", "session", 10))).Events);
    }

    [Fact]
    public async Task Legacy_records_without_ownership_are_ineligible_but_remain_in_the_trusted_read()
    {
        var store = new InMemoryAgentAuditStore();
        await store.AssociateAsync("owned", "actor-a", "tenant-1");
        await EmitAsync(store, "legacy-session", "legacy", T0.AddMinutes(5));
        await EmitAsync(store, "no-session", null, T0.AddMinutes(6));
        // An event naming the caller as actor does not grant ownership of an unassociated session.
        await store.EmitAsync(new("legacy-actor", AgentAuditEventKind.ToolInvoked, "legacy", "actor-a", "Legacy.", T0.AddMinutes(7), new Dictionary<string, string>()));
        await EmitAsync(store, "owned-1", "owned", T0);

        Assert.Equal(["owned-1"], (await store.ListOwnedAsync(new("actor-a", "tenant-1", null, 1))).Events.Select(x => x.Id));
        Assert.Empty((await store.ListOwnedAsync(new("actor-a", "tenant-1", "legacy", 10))).Events);
        Assert.Equal(4, (await store.ListAsync()).Count);
        Assert.Equal(2, (await store.ListAsync("legacy")).Count);
    }

    [Theory]
    [InlineData("actor-a", "tenant-1", 0)]
    [InlineData("actor-a", "tenant-1", AgentAuditOwnerQuery.MaxTake + 1)]
    [InlineData(" ", "tenant-1", 1)]
    [InlineData("actor-a", "", 1)]
    public async Task An_unbounded_or_unowned_query_is_refused_by_the_reader(string actor, string tenant, int take)
    {
        var store = new InMemoryAgentAuditStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new(actor, tenant, null, take)));
    }

    [Fact]
    public async Task A_reader_replacement_without_the_owned_query_reports_unsupported()
    {
        IAgentAuditReader reader = new TrustedOnlyReader();

        var result = await reader.ListOwnedAsync(new("actor-a", "tenant-1", null, 1));

        Assert.Equal(AgentAuditQueryOutcome.Unsupported, result.Outcome);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task The_default_session_owner_associates_each_new_session_with_the_same_store_without_a_cycle()
    {
        using var root = AgentTestServices.Create();
        var store = root.GetRequiredService<InMemoryAgentAuditStore>();
        Assert.Same(store, root.GetRequiredService<IAgentAuditSink>());
        Assert.Same(store, root.GetRequiredService<IAgentAuditReader>());
        Assert.Same(store, root.GetRequiredService<IAgentAuditOwnership>());
        var sessions = root.GetRequiredService<IAgentSessionService>();
        var mine = await sessions.CreateAsync(Request("actor-a", "tenant-1"));
        var foreign = await sessions.CreateAsync(Request("actor-b", "tenant-1"));

        var owned = await root.GetRequiredService<IAgentAuditReader>().ListOwnedAsync(new("actor-a", "tenant-1", null, 10));

        var created = Assert.Single(owned.Events);
        Assert.Equal(AgentAuditEventKind.SessionCreated, created.Kind);
        Assert.Equal(mine.Id, created.SessionId);
        Assert.False(await store.AssociateAsync(mine.Id, "actor-b", "tenant-1"));
        Assert.DoesNotContain(owned.Events, x => x.SessionId == foreign.Id);
    }

    [Fact]
    public async Task A_session_whose_identity_is_already_owned_elsewhere_is_not_created()
    {
        var store = new InMemoryAgentAuditStore();
        var sessions = new InMemoryAgentSessionService(store, TimeProvider.System, new RefusingOwnership());

        await Assert.ThrowsAsync<AgentContributionException>(() => sessions.CreateAsync(Request("actor-a", "tenant-1")));

        Assert.Empty(await store.ListAsync());
    }

    private static AgentSessionCreateRequest Request(string actor, string tenant) => new(tenant, actor, "conversation-1", "test-provider", "explain", null,
        AgentPolicy.Default, new Dictionary<string, string>());

    private static Task EmitAsync(InMemoryAgentAuditStore store, string id, string? sessionId, DateTimeOffset at)
        => store.EmitAsync(new(id, AgentAuditEventKind.ToolInvoked, sessionId, null, "Test record.", at, new Dictionary<string, string>()));

    private sealed class TrustedOnlyReader : IAgentAuditReader
    {
        public Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An owner query must not fall back to the trusted global read.");
    }

    private sealed class RefusingOwnership : IAgentAuditOwnership
    {
        public Task<bool> AssociateAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
