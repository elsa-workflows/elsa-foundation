using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Preview (#3441 T025): the two documented session limits carry a distinct <c>limit</c> detail, and reaching one, including in
/// the middle of a turn, keeps every recorded outcome, never erases provenance, never resets the budget and never replays.
/// </summary>
public sealed partial class WeaverCumulativeDisclosureTests
{
    private const string BudgetExceeded = "weaver.context.disclosure_budget_exceeded";

    [Fact]
    public void Each_limit_names_itself_on_the_refusal()
    {
        var counted = new WeaverDisclosureHistory();
        for (var index = 0; index < WeaverDisclosureHistory.MaximumSnapshots; index++)
            Assert.Null(counted.Add([HistoryAttachment("same", new { index })], []));
        var count = counted.Add([HistoryAttachment("same", new { index = 65 })], []);
        Assert.Equal((BudgetExceeded, "snapshots", 409), (count?.Code, count?.Limit, count?.StatusCode));

        // Genuinely full: less than the documented minimum room is left when the next snapshot does not fit.
        var sized = new WeaverDisclosureHistory();
        Assert.Null(sized.Add([HistoryAttachment("first", new { value = new string('x', WeaverDisclosureHistory.MaximumBytes - 40_000) })], []));
        var size = sized.Add([HistoryAttachment("second", new { value = new string('y', 50_000) })], []);
        Assert.Equal((BudgetExceeded, "bytes", 409), (size?.Code, size?.Limit, size?.StatusCode));
        Assert.Contains("session limit", size!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_snapshot_that_does_not_fit_the_room_left_in_a_session_that_is_not_full_names_no_session_limit()
    {
        var history = new WeaverDisclosureHistory();
        Assert.Null(history.Add([HistoryAttachment("first", new { value = new string('x', 1024 * 1024) })], []));

        var refused = history.Add([HistoryAttachment("large", new { value = new string('y', 7 * 1024 * 1024 + 512 * 1024) })], []);

        Assert.Equal((BudgetExceeded, WeaverDisclosureHistory.SnapshotDoesNotFit, 409), (refused?.Code, refused?.Limit, refused?.StatusCode));
        Assert.DoesNotContain("session limit", refused!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("of 8 MiB used", refused.Message, StringComparison.Ordinal);
        // Nothing was counted: a snapshot that fits is still admitted.
        Assert.Null(history.Add([HistoryAttachment("fits", new { value = new string('z', 1024 * 1024) })], []));
    }

    // A context attachment is bounded (32 KB of content), so the session is filled with many bounded snapshots, and a turn
    // discloses two or three attachments of 30 000 characters each.
    [Theory]
    [InlineData(70_000, true, "snapshot-does-not-fit", null)]
    [InlineData(40_000, false, "bytes", "bytes")]
    public async Task A_turn_records_the_bytes_limit_only_when_the_session_is_genuinely_full(int room, bool threeAttachments, string limit, string? recorded)
    {
        using var fixture = new Fixture(true, Script.Answer);
        var left = await SeedRetainedBytes(fixture, room);
        Assert.InRange(left, room - 1_000, room);
        Assert.Equal(threeAttachments, left >= WeaverDisclosureHistory.MinimumRoom);
        fixture.Source.Padding = 30_000;
        fixture.Source.AddSection = threeAttachments;
        await fixture.Post("Request the current projection");

        Assert.Equal(limit, Assert.Single(await fixture.Stream(), item => item.Error?.Code == BudgetExceeded).Error!.Limit);

        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        Assert.Equal(recorded, (await sessions.FindAsync(fixture.Session!.Id))!.LimitReached);
        Assert.Equal(WeaverSessionRecoveryStates.Of(recorded),
            Assert.Single((await sessions.ListOwnedAsync(new(fixture.Session.ActorId, fixture.Session.TenantId, null, 20, null))).Page!.Items).RecoveryState);
    }

    /// <summary>
    /// Records an earlier completed exchange whose distinct snapshots of bounded attachments leave about <paramref name="room"/>
    /// bytes of the session's disclosure budget; returns the exact room left.
    /// </summary>
    private static async Task<long> SeedRetainedBytes(Fixture fixture, int room)
    {
        await fixture.Post("An earlier authorized request");
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        var original = Assert.Single(await sessions.ListMessagesAsync(fixture.Session!.Id));
        await sessions.SetMessageStatusAsync(fixture.Session.Id, original.Id, WeaverMessageStatus.Completed);
        var attachment = Assert.Single(original.Context);
        WeaverDisclosureSnapshot Snapshot(int index, IReadOnlyList<int> pads) => new(pads.Select((pad, item) =>
            attachment with { Id = $"seed-{index}-{item}", Content = new { index, item, pad = new string('p', pad) } }).ToArray(), []);
        long Size(WeaverDisclosureSnapshot snapshot) => WeaverDisclosureHistory.Record(snapshot).Size;
        var target = WeaverDisclosureHistory.MaximumBytes - room;
        var snapshots = new List<WeaverDisclosureSnapshot>();
        long total = 0;
        while (true)
        {
            var full = Snapshot(snapshots.Count, Enumerable.Repeat(30_000, 6).ToArray());
            if (total + Size(full) > target) break;
            snapshots.Add(full);
            total += Size(full);
        }
        // The last snapshot fills the remainder: whole attachments, then one sized by its measured overhead.
        var remainder = target - total;
        var pads = new List<int>();
        var perAttachment = Size(Snapshot(snapshots.Count, [30_000])) - Size(Snapshot(snapshots.Count, [0]));
        while (remainder - (pads.Count + 1) * perAttachment > 1_000) pads.Add(30_000);
        pads.Add(0);
        var partial = (int)(remainder - Size(Snapshot(snapshots.Count, pads)));
        pads[^1] = Math.Max(0, partial);
        snapshots.Add(Snapshot(snapshots.Count, pads));
        total = snapshots.Sum(Size);
        var history = WeaverDisclosureHistory.Freeze(snapshots);
        const string priorAnswer = "The authorized earlier snapshots were observed.";
        await sessions.AddMessageAsync(fixture.Session.Id, new(WeaverRole.Assistant, priorAnswer, WeaverMessageStatus.Completed, null, [], [])
        {
            HasDisclosureProvenance = true, DisclosureHistory = history,
            DeliveryFingerprint = WeaverDisclosureHistory.MessageFingerprint(priorAnswer, [], [], history)
        });
        return WeaverDisclosureHistory.MaximumBytes - total;
    }

    [Fact]
    public void A_single_snapshot_larger_than_any_session_can_record_is_refused_without_naming_a_session_limit()
    {
        var history = new WeaverDisclosureHistory();
        Assert.Null(history.Add([HistoryAttachment("first", new { value = "retained" })], []));

        var refused = history.Add([HistoryAttachment("too-large", new { value = new string('x', WeaverDisclosureHistory.MaximumBytes) })], []);

        Assert.Equal((BudgetExceeded, WeaverDisclosureHistory.SnapshotTooLarge, 409), (refused?.Code, refused?.Limit, refused?.StatusCode));
        Assert.DoesNotContain("session limit", refused!.Message, StringComparison.OrdinalIgnoreCase);
        // Nothing was counted against the session: a later snapshot of ordinary size is admitted.
        Assert.Null(history.Add([HistoryAttachment("ordinary", new { value = "fits" })], []));
    }

    [Fact]
    public async Task A_reached_limit_stays_recorded_after_a_later_turn_that_adds_no_snapshot_completes()
    {
        using var fixture = new Fixture(true, Script.Answer);
        await SeedRetainedHistory(fixture, WeaverDisclosureHistory.MaximumSnapshots);
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        await fixture.Post("Request the current expanded projection");
        Assert.Equal("snapshots", Assert.Single(await fixture.Stream(), item => item.Error?.Code == BudgetExceeded).Error!.Limit);
        foreach (var message in (await sessions.ListMessagesAsync(fixture.Session!.Id)).Where(x => x.Role == WeaverRole.User && x.Status is WeaverMessageStatus.Pending or WeaverMessageStatus.Streaming))
            await sessions.SetMessageStatusAsync(fixture.Session.Id, message.Id, WeaverMessageStatus.Failed);

        // A turn that discloses only an already recorded snapshot completes, but the session is still full.
        fixture.Source.ShowDetail = false;
        await fixture.Post("Answer from what is already recorded");
        Assert.Equal(WeaverStreamEventKind.Completed, (await fixture.Stream())[^1].Kind);

        Assert.Equal("snapshots", (await sessions.FindAsync(fixture.Session.Id))!.LimitReached);
        var listed = await sessions.ListOwnedAsync(new(fixture.Session.ActorId, fixture.Session.TenantId, null, 20, null));
        Assert.Equal(WeaverSessionRecoveryStates.LimitReachedPrefix + "snapshots", Assert.Single(listed.Page!.Items).RecoveryState);
    }

    [Fact]
    public async Task A_limit_reached_before_the_provider_names_its_limit_on_the_stream_and_the_wire()
    {
        using var fixture = new Fixture(true, Script.Answer, services => services.AddRouting().AddLogging().AddElsaEndpoints());
        await SeedRetainedHistory(fixture, WeaverDisclosureHistory.MaximumSnapshots);
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
        await SeedRetainedHistory(fixture, WeaverDisclosureHistory.MaximumSnapshots);
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        Assert.Null((await sessions.FindAsync(fixture.Session!.Id))!.LimitReached);
        await fixture.Post("Request the current expanded projection");

        Assert.Equal("snapshots", Assert.Single(await fixture.Stream(), item => item.Error?.Code == BudgetExceeded).Error!.Limit);

        Assert.Equal("snapshots", (await sessions.FindAsync(fixture.Session.Id))!.LimitReached);
        var listed = await sessions.ListOwnedAsync(new(fixture.Session.ActorId, fixture.Session.TenantId, null, 20, null));
        Assert.Equal(WeaverSessionRecoveryStates.LimitReachedPrefix + "snapshots", Assert.Single(listed.Page!.Items).RecoveryState);
    }

    [Fact]
    public async Task A_limit_reached_mid_turn_keeps_the_executed_result_and_provenance_and_never_resets_or_replays()
    {
        using var fixture = new Fixture(true, Script.ReadThenAnswer);
        await SeedRetainedHistory(fixture, WeaverDisclosureHistory.MaximumSnapshots - 1);
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        var retained = (await sessions.ListMessagesAsync(fixture.Session!.Id)).Single(x => x.Role == WeaverRole.Assistant);
        var retainedDigest = WeaverInvocationIntegrity.Digest(retained.DisclosureHistory!);
        await fixture.Post("Read another resource, then answer");

        var events = await fixture.Stream();

        // The read ran once and the limit stopped the turn after it, with the limit named.
        Assert.Equal(1, fixture.Read.Executions);
        var stop = Assert.Single(events, item => item.Error?.Code == BudgetExceeded).Error!;
        Assert.Equal("snapshots", stop.Limit);
        Assert.DoesNotContain(events, item => item.Kind == WeaverStreamEventKind.Completed);
        Assert.False(fixture.Probe.AfterCallbackEntered);
        // The executed tool result's transcript record is kept; the earlier provenance is unchanged.
        var messages = await sessions.ListMessagesAsync(fixture.Session.Id);
        var recorded = Assert.Single(messages, x => x.Role == WeaverRole.Tool);
        Assert.Equal("cumulative_neutral_read", Assert.Single(recorded.ToolResults).ToolName);
        var stillRetained = messages.Single(x => x.Id == retained.Id);
        Assert.Equal(retainedDigest, WeaverInvocationIntegrity.Digest(stillRetained.DisclosureHistory!));

        // The budget is not reset: a later prompt is refused again, before the provider, with nothing replayed.
        foreach (var message in messages.Where(x => x.Role == WeaverRole.User && x.Status is WeaverMessageStatus.Pending or WeaverMessageStatus.Streaming))
            await sessions.SetMessageStatusAsync(fixture.Session.Id, message.Id, WeaverMessageStatus.Failed);
        var inputs = fixture.Probe.Inputs.Count;
        await fixture.Post("Try once more in the same session");
        var again = await fixture.Stream();
        Assert.Equal("snapshots", Assert.Single(again, item => item.Error?.Code == BudgetExceeded).Error!.Limit);
        Assert.Equal(inputs, fixture.Probe.Inputs.Count);
        Assert.Equal(1, fixture.Read.Executions);
        var afterRefusal = await sessions.ListMessagesAsync(fixture.Session.Id);
        Assert.Single(afterRefusal, x => x.Role == WeaverRole.Tool);
        Assert.Equal(retainedDigest, WeaverInvocationIntegrity.Digest(afterRefusal.Single(x => x.Id == retained.Id).DisclosureHistory!));
    }

    [Fact]
    public async Task A_continuation_session_links_the_original_and_carries_nothing_else()
    {
        using var fixture = new Fixture(true, Script.Answer);
        await SeedRetainedHistory(fixture, WeaverDisclosureHistory.MaximumSnapshots);
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        var original = fixture.Session!;

        var continued = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-2", "cumulative-probe", "explain", null,
            fixture.Policy, new Dictionary<string, string>()) { ContinuedFrom = original.Id });

        Assert.Equal(original.Id, continued.ContinuedFrom);
        Assert.Empty(await sessions.ListMessagesAsync(continued.Id));
        Assert.Empty(await sessions.ListContextAsync(continued.Id));
        // The original stays readable with its provenance intact.
        Assert.Contains(await sessions.ListMessagesAsync(original.Id), x => x.DisclosureHistory is { Count: WeaverDisclosureHistory.MaximumSnapshots });
    }

    /// <summary>Records an earlier completed exchange whose assistant answer retains <paramref name="retained"/> distinct snapshots.</summary>
    private static async Task SeedRetainedHistory(Fixture fixture, int retained)
    {
        await fixture.Post("An earlier authorized request");
        var sessions = fixture.Services.GetRequiredService<IWeaverSessionService>();
        var original = Assert.Single(await sessions.ListMessagesAsync(fixture.Session!.Id));
        await sessions.SetMessageStatusAsync(fixture.Session.Id, original.Id, WeaverMessageStatus.Completed);
        var attachment = Assert.Single(original.Context);
        var history = WeaverDisclosureHistory.Freeze(Enumerable.Range(0, retained).Select(index => new WeaverDisclosureSnapshot(
            [index == 0 ? attachment : attachment with { Content = new { snapshot = index } }], [])));
        const string priorAnswer = "The authorized earlier snapshots were observed.";
        await sessions.AddMessageAsync(fixture.Session.Id, new(WeaverRole.Assistant, priorAnswer, WeaverMessageStatus.Completed, null, [], [])
        {
            HasDisclosureProvenance = true, DisclosureHistory = history,
            DeliveryFingerprint = WeaverDisclosureHistory.MessageFingerprint(priorAnswer, [], [], history)
        });
    }
}
