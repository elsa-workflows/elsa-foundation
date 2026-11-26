using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Controlled API owners; physical timer/journal pairing is qualified by the provider controls.
public sealed partial class RuntimeHistoryApiTests
{
    private static SourceProjection TimerProjection()
    {
        var projection = new SourceProjection { Items = [] };
        projection.Head = projection.Head with { Policy = new("runtime-history-v4", 4, true, DurableTimerHistoryFacts.Kinds) };
        projection.Refreshed = projection.Head;
        return projection;
    }

    [Theory]
    [InlineData("missing-owner")] [InlineData("missing-row")] [InlineData("unavailable")]
    public async Task Known_pending_timer_responsibility_survives_unknown_outbox_evidence(string boundary)
    {
        var projection = new SourceProjection();
        projection.Head = projection.Head with { Policy = new("runtime-history-v4", 4, true,
            SourcePolicy.SupportedKinds.Concat(DurableTimerHistoryFacts.Kinds).ToArray()) };
        projection.Refreshed = projection.Head;
        var owner = new TimerLookup(projection) { Pending = true };
        var lookup = boundary == "missing-owner" ? null : new SourceLookup(projection);
        if (boundary == "missing-row") lookup!.Item = null;
        if (boundary == "unavailable") lookup!.Failure = new IOException("private source details");
        using var provider = await SourceServices(projection, lookup, resolveTimer: () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.True(view.Coverage.SourceHistoryPending);
        Assert.Equal("pending", view.Coverage.State);
        Assert.NotNull(view.Coverage.SourceHistoryLimitation);
        Assert.DoesNotContain("private", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(new[] { "run" }, owner.Reads);
    }

    [Theory]
    [InlineData("pair")] [InlineData("support")] [InlineData("actor")]
    public async Task Pending_timer_witness_is_revalidated_after_an_outbox_failure(string boundary)
    {
        var projection = new SourceProjection();
        projection.Head = projection.Head with { Policy = new("runtime-history-v4", 4, true,
            SourcePolicy.SupportedKinds.Concat(DurableTimerHistoryFacts.Kinds).ToArray()) };
        projection.Refreshed = projection.Head;
        var owner = new TimerLookup(projection) { Pending = true }; var auth = new Authorization();
        var lookup = new SourceLookup(projection) { Failure = new IOException("private source details"), BeforeFailure = () =>
            { if (boundary == "pair") owner.Paired = false; else if (boundary == "support") owner.Supported = false; else auth.Subject = "replacement"; } };
        using var provider = await SourceServices(projection, lookup, auth, resolveTimer: () => owner);
        var reading = provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        if (boundary == "actor") { await Assert.ThrowsAsync<RuntimeResourceMissingSignal>(() => reading); return; }
        var view = await reading;
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Equal("partial", view.Coverage.State);
        Assert.NotNull(view.Coverage.SourceHistoryLimitation); Assert.DoesNotContain("private", view.Coverage.SourceHistoryLimitation);
    }

    [Fact]
    public async Task Pending_timer_witness_does_not_soften_malformed_outbox_evidence()
    {
        var projection = new SourceProjection();
        projection.Head = projection.Head with { Policy = new("runtime-history-v4", 4, true,
            SourcePolicy.SupportedKinds.Concat(DurableTimerHistoryFacts.Kinds).ToArray()) };
        projection.Refreshed = projection.Head;
        var owner = new TimerLookup(projection) { Pending = true };
        using var provider = await SourceServices(projection, new SourceLookup(projection) { Failure = new InvalidDataException("private") }, resolveTimer: () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal("invalid", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.Empty(view.Items); Assert.Null(view.CatchUpToken);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_settlement_uses_the_exact_owner_without_reading_due_timers(bool pending)
    {
        var projection = TimerProjection(); var owner = new TimerLookup(projection) { Pending = pending };
        using var provider = await SourceServices(projection, resolveTimer: () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal(pending, view.Coverage.SourceHistoryPending);
        Assert.Equal(pending ? "pending" : "captured", view.Coverage.State);
        Assert.Null(view.Coverage.SourceHistoryLimitation);
        Assert.Equal(new[] { "run" }, owner.Reads); Assert.Single(projection.Queries);
        Assert.Equal(pending ? 0 : 1, projection.HeadReads);
    }

    [Theory]
    [InlineData("missing")] [InlineData("unsupported")] [InlineData("foreign")] [InlineData("partial")]
    public async Task Timer_settlement_keeps_unknown_or_incomplete_owner_evidence_unknown(string boundary)
    {
        var projection = TimerProjection(); var owner = new TimerLookup(projection);
        if (boundary == "unsupported") owner.Supported = false;
        if (boundary == "foreign") owner.Paired = false;
        if (boundary == "partial") projection.Head = projection.Head with { Policy = new("runtime-history-v4", 4, true, ["TimerScheduled"]) };
        using var provider = await SourceServices(projection, resolveTimer: boundary == "missing" ? null : () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Equal("partial", view.Coverage.State);
        Assert.NotNull(view.Coverage.SourceHistoryLimitation); Assert.Empty(owner.Reads);
    }

    [Fact]
    public async Task Timer_owner_is_not_resolved_before_structure_authorization()
    {
        var projection = TimerProjection(); var resolutions = 0;
        using var provider = await SourceServices(projection, auth: new Authorization { Allowed = false },
            resolveTimer: () => { resolutions++; throw new IOException("must not resolve"); });
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run")));
        Assert.Equal(0, resolutions); Assert.Empty(projection.Queries);
    }

    [Theory]
    [InlineData("pair")] [InlineData("support")] [InlineData("actor")]
    public async Task Timer_settlement_refuses_authority_changed_during_owner_read(string boundary)
    {
        var projection = TimerProjection(); var owner = new TimerLookup(projection); var auth = new Authorization();
        owner.AfterRead = () => { if (boundary == "pair") owner.Paired = false; else if (boundary == "support") owner.Supported = false; else auth.Subject = "replacement"; };
        using var provider = await SourceServices(projection, auth: auth, resolveTimer: () => owner);
        var reading = provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        if (boundary == "actor")
        {
            await Assert.ThrowsAsync<RuntimeResourceMissingSignal>(() => reading);
            Assert.Equal(0, projection.HeadReads);
            return;
        }
        var view = await reading;
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Contains("context changed", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(0, projection.HeadReads);
    }

    [Theory]
    [InlineData("pair")] [InlineData("support")] [InlineData("actor")]
    public async Task Timer_negative_observation_rechecks_authority_after_the_head_read(string boundary)
    {
        var projection = TimerProjection(); var owner = new TimerLookup(projection); var auth = new Authorization();
        projection.BeforeHeadRead = () => { if (boundary == "pair") owner.Paired = false; else if (boundary == "support") owner.Supported = false; else auth.Subject = "replacement"; };
        using var provider = await SourceServices(projection, auth: auth, resolveTimer: () => owner);
        var reading = provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        if (boundary == "actor")
        {
            await Assert.ThrowsAsync<RuntimeResourceMissingSignal>(() => reading);
            Assert.Equal(1, projection.HeadReads);
            return;
        }
        var view = await reading;
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Contains("context changed", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(1, projection.HeadReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_owner_unavailability_keeps_recorded_history_readable(bool independentCancellation)
    {
        var projection = TimerProjection(); var owner = new TimerLookup(projection)
            { Failure = independentCancellation ? new OperationCanceledException("secret") : new IOException("secret") };
        using var provider = await SourceServices(projection, resolveTimer: () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Equal("partial", view.Coverage.State);
        Assert.DoesNotContain("secret", view.Coverage.SourceHistoryLimitation); Assert.Equal(10, view.UpperSequence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_owner_malformed_evidence_remains_a_hard_integrity_failure(bool wrapped)
    {
        var projection = TimerProjection(); var malformed = new InvalidDataException("bad timer bytes");
        var owner = new TimerLookup(projection) { Failure = wrapped ? new InvalidOperationException("owner", malformed) : malformed };
        using var provider = await SourceServices(projection, resolveTimer: () => owner);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal("invalid", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.DoesNotContain("bad timer bytes", view.Coverage.Limitation);
    }

    [Fact]
    public async Task Timer_owner_requested_cancellation_preserves_the_callers_token()
    {
        using var cancellation = new CancellationTokenSource();
        var projection = TimerProjection(); var owner = new TimerLookup(projection) { AfterRead = cancellation.Cancel };
        using var provider = await SourceServices(projection, resolveTimer: () => owner);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private sealed class TimerLookup(SourceProjection projection) : IDurableTimerHistoryStore
    {
        public bool Supported { get; set; } = true;
        public bool Paired { get; set; } = true;
        public bool Pending { get; set; }
        public Exception? Failure { get; set; }
        public Action? AfterRead { get; set; }
        public List<string> Reads { get; } = [];
        public bool SupportsHistoryResponsibility => Supported;
        public bool CanInspectHistorySources(IRuntimeHistoryStore reader) => Paired && ReferenceEquals(reader, projection);
        public ValueTask<bool> HasPendingHistoryAsync(string run, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Reads.Add(run); if (Failure is { } failure) throw failure; AfterRead?.Invoke(); token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Pending); }
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken token = default) => throw new InvalidOperationException("No operational timer writes.");
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset at, int limit, CancellationToken token = default) => throw new InvalidOperationException("Due timers cannot establish history settlement.");
        public ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageAsync(DateTimeOffset asOf, RuntimeStorePageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Due timers cannot establish history settlement.");
        public ValueTask<DurableTimer?> FindAsync(string run, string timer, CancellationToken token = default) => throw new InvalidOperationException("No unbounded owner lookup.");
        public ValueTask DeleteAsync(string run, string timer, CancellationToken token = default) => throw new InvalidOperationException("No operational timer writes.");
        public ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken token = default) => throw new InvalidOperationException("No source enumeration.");
        public ValueTask<bool> AcknowledgeHistoryAsync(string run, string timer, string item, CancellationToken token = default) => throw new InvalidOperationException("No source acknowledgement.");
        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim, RuntimeHistoryObservation observation, CancellationToken token = default) => throw new InvalidOperationException("No claim writes.");
        public ValueTask RetireWithHistoryAsync(string run, string timer, RuntimeHistoryObservation observation, CancellationToken token = default) => throw new InvalidOperationException("No retirement writes.");
    }
}
