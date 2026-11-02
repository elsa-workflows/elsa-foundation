using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// Preview (#3441 T025): the two documented session limits carry a distinct <c>limit</c> detail, and reaching one, including in
/// the middle of a turn, keeps every recorded outcome, never erases provenance, never resets the budget and never replays.
/// </summary>
public sealed partial class AgentCumulativeDisclosureTests
{
    private const string BudgetExceeded = "agent.context.disclosure_budget_exceeded";

    [Fact]
    public void Each_limit_names_itself_on_the_refusal()
    {
        var counted = new AgentDisclosureHistory();
        for (var index = 0; index < AgentDisclosureHistory.MaximumSnapshots; index++)
            Assert.Null(counted.Add([HistoryAttachment("same", new { index })], []));
        var count = counted.Add([HistoryAttachment("same", new { index = 65 })], []);
        Assert.Equal((BudgetExceeded, "snapshots", 409), (count?.Code, count?.Limit, count?.StatusCode));

        var sized = new AgentDisclosureHistory();
        Assert.Null(sized.Add([HistoryAttachment("first", new { value = "retained" })], []));
        var size = sized.Add([HistoryAttachment("too-large", new { value = new string('x', AgentDisclosureHistory.MaximumBytes) })], []);
        Assert.Equal((BudgetExceeded, "bytes", 409), (size?.Code, size?.Limit, size?.StatusCode));
        Assert.Contains("session limit", size!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_limit_reached_before_the_provider_names_its_limit_on_the_stream_and_the_wire()
    {
        using var fixture = new Fixture(true, Script.Answer, services => services.AddRouting().AddLogging().AddElsaEndpoints());
        await SeedRetainedHistory(fixture, AgentDisclosureHistory.MaximumSnapshots);
        await fixture.Post("Request the current expanded projection");

        var wire = await fixture.StreamViaApi();

        Assert.Contains($"\"code\":\"{BudgetExceeded}\"", wire);
        Assert.Contains("\"limit\":\"snapshots\"", wire);
        Assert.Empty(fixture.Probe.Inputs);
    }

    [Fact]
    public async Task A_limit_reached_is_recorded_on_the_session_so_a_reload_reports_it_before_another_prompt()
    {
        using var fixture = new Fixture(true, Script.Answer);
        await SeedRetainedHistory(fixture, AgentDisclosureHistory.MaximumSnapshots);
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        Assert.Null((await sessions.FindAsync(fixture.Session!.Id))!.LimitReached);
        await fixture.Post("Request the current expanded projection");

        Assert.Equal("snapshots", Assert.Single(await fixture.Stream(), item => item.Error?.Code == BudgetExceeded).Error!.Limit);

        Assert.Equal("snapshots", (await sessions.FindAsync(fixture.Session.Id))!.LimitReached);
        var listed = await sessions.ListOwnedAsync(new(fixture.Session.ActorId, fixture.Session.TenantId, null, 20, null));
        Assert.Equal(AgentSessionRecoveryStates.LimitReachedPrefix + "snapshots", Assert.Single(listed.Page!.Items).RecoveryState);
    }

    [Fact]
    public async Task A_limit_reached_mid_turn_keeps_the_executed_result_and_provenance_and_never_resets_or_replays()
    {
        using var fixture = new Fixture(true, Script.ReadThenAnswer);
        await SeedRetainedHistory(fixture, AgentDisclosureHistory.MaximumSnapshots - 1);
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        var retained = (await sessions.ListMessagesAsync(fixture.Session!.Id)).Single(x => x.Role == AgentRole.Assistant);
        var retainedDigest = AgentInvocationIntegrity.Digest(retained.DisclosureHistory!);
        await fixture.Post("Read another resource, then answer");

        var events = await fixture.Stream();

        // The read ran once and the limit stopped the turn after it, with the limit named.
        Assert.Equal(1, fixture.Read.Executions);
        var stop = Assert.Single(events, item => item.Error?.Code == BudgetExceeded).Error!;
        Assert.Equal("snapshots", stop.Limit);
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.False(fixture.Probe.AfterCallbackEntered);
        // The executed tool result's transcript record is kept; the earlier provenance is unchanged.
        var messages = await sessions.ListMessagesAsync(fixture.Session.Id);
        var recorded = Assert.Single(messages, x => x.Role == AgentRole.Tool);
        Assert.Equal("cumulative_neutral_read", Assert.Single(recorded.ToolResults).ToolName);
        var stillRetained = messages.Single(x => x.Id == retained.Id);
        Assert.Equal(retainedDigest, AgentInvocationIntegrity.Digest(stillRetained.DisclosureHistory!));

        // The budget is not reset: a later prompt is refused again, before the provider, with nothing replayed.
        foreach (var message in messages.Where(x => x.Role == AgentRole.User && x.Status is AgentMessageStatus.Pending or AgentMessageStatus.Streaming))
            await sessions.SetMessageStatusAsync(fixture.Session.Id, message.Id, AgentMessageStatus.Failed);
        var inputs = fixture.Probe.Inputs.Count;
        await fixture.Post("Try once more in the same session");
        var again = await fixture.Stream();
        Assert.Equal("snapshots", Assert.Single(again, item => item.Error?.Code == BudgetExceeded).Error!.Limit);
        Assert.Equal(inputs, fixture.Probe.Inputs.Count);
        Assert.Equal(1, fixture.Read.Executions);
        var afterRefusal = await sessions.ListMessagesAsync(fixture.Session.Id);
        Assert.Single(afterRefusal, x => x.Role == AgentRole.Tool);
        Assert.Equal(retainedDigest, AgentInvocationIntegrity.Digest(afterRefusal.Single(x => x.Id == retained.Id).DisclosureHistory!));
    }

    [Fact]
    public async Task A_continuation_session_links_the_original_and_carries_nothing_else()
    {
        using var fixture = new Fixture(true, Script.Answer);
        await SeedRetainedHistory(fixture, AgentDisclosureHistory.MaximumSnapshots);
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        var original = fixture.Session!;

        var continued = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-2", "cumulative-probe", "explain", null,
            fixture.Policy, new Dictionary<string, string>()) { ContinuedFrom = original.Id });

        Assert.Equal(original.Id, continued.ContinuedFrom);
        Assert.Empty(await sessions.ListMessagesAsync(continued.Id));
        Assert.Empty(await sessions.ListContextAsync(continued.Id));
        // The original stays readable with its provenance intact.
        Assert.Contains(await sessions.ListMessagesAsync(original.Id), x => x.DisclosureHistory is { Count: AgentDisclosureHistory.MaximumSnapshots });
    }

    /// <summary>Records an earlier completed exchange whose assistant answer retains <paramref name="retained"/> distinct snapshots.</summary>
    private static async Task SeedRetainedHistory(Fixture fixture, int retained)
    {
        await fixture.Post("An earlier authorized request");
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        var original = Assert.Single(await sessions.ListMessagesAsync(fixture.Session!.Id));
        await sessions.SetMessageStatusAsync(fixture.Session.Id, original.Id, AgentMessageStatus.Completed);
        var attachment = Assert.Single(original.Context);
        var history = AgentDisclosureHistory.Freeze(Enumerable.Range(0, retained).Select(index => new AgentDisclosureSnapshot(
            [index == 0 ? attachment : attachment with { Content = new { snapshot = index } }], [])));
        const string priorAnswer = "The authorized earlier snapshots were observed.";
        await sessions.AddMessageAsync(fixture.Session.Id, new(AgentRole.Assistant, priorAnswer, AgentMessageStatus.Completed, null, [], [])
        {
            HasDisclosureProvenance = true, DisclosureHistory = history,
            DeliveryFingerprint = AgentDisclosureHistory.MessageFingerprint(priorAnswer, [], [], history)
        });
    }
}
