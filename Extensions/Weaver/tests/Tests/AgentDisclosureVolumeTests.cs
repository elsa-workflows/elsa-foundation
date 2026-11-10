using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 story 3 (FR-014, FR-015): recorded disclosure history and owner re-admission grow with what was actually
/// disclosed, not with every checkpoint. A session runs at least 40 checkpoints that alternate a repeated snapshot and a
/// new one; the stored snapshot copies and the owner admissions of each authorization pass are measured.
/// </summary>
public sealed class AgentDisclosureVolumeTests(ITestOutputHelper output)
{
    private const string Module = "volume.module";
    private const string SourceId = "volume.context";
    private const string Marker = "volume-marker:";
    private const int Turns = 40;

    /// <remarks>
    /// T023 recorded the baseline on the earlier representation, where every checkpoint stored the complete cumulative
    /// list and every pass re-admitted each stored copy: from turn 20 to turn 40 the stored copies went 318 to 1038 and
    /// the largest pass 306 to 1016, with 1966 repeated admissions within the passes of the last turn.
    /// </remarks>
    [Fact]
    public async Task Stored_snapshots_and_owner_admissions_per_pass_grow_with_distinct_disclosures_not_with_checkpoints()
    {
        var measured = await MeasureAsync(Turns);

        // Each turn writes one checkpoint and one final journal, and every one of them references the cumulative history.
        Assert.True(measured[^1].Checkpoints >= 40);
        var half = measured[Turns / 2 - 1];
        var full = measured[^1];
        // Each distinct snapshot is recorded once, however many checkpoints reference it.
        Assert.All(measured, item => Assert.Equal(item.DistinctSnapshots, item.RecordedSnapshots));
        // Linear: the second half of the session adds no more than the first half did.
        Assert.True(full.StoredCopies - half.StoredCopies <= half.StoredCopies, $"stored copies {half.StoredCopies} -> {full.StoredCopies}");
        // Within one pass the owner is asked once per distinct snapshot, so a pass grows with the distinct snapshots only.
        Assert.All(measured, item => Assert.Equal(0, item.RepeatedInPass));
        Assert.All(measured, item => Assert.True(item.LargestPass <= item.DistinctSnapshots + 1, $"turn {item.Turn}: largest pass {item.LargestPass}"));
    }

    [Fact]
    public async Task Each_checkpoint_write_ends_the_pass_so_withdrawn_authority_stops_the_provider_call()
    {
        CountingRecordStore? store = null;
        var source = new LedgerSource(() => store!.Writes);
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)),
            services => services.AddSingleton<IAgentContextProvider>(source));
        var host = harness.Host;
        var session = await host.CreateAsync();
        source.Current = "repeated";
        await PostAsync(host, session, "First question");
        var start = store!.Writes;
        source.Admissions.Clear();
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        // Writes: the prompt's start (start + 1), the checkpoint (start + 2), then the final journal. The checkpoint write ends
        // the step's pass: the current context the step admitted before it is asked again after it, before the provider is
        // called, and nothing is admitted after the write that the step had not admitted before it.
        var beforeWrite = source.Admissions.Where(x => x.Epoch == start + 1).Select(x => x.Key).ToHashSet();
        var afterWrite = source.Admissions.Where(x => x.Epoch == start + 2).Select(x => x.Key).ToHashSet();
        Assert.NotEmpty(beforeWrite);
        Assert.NotEmpty(afterWrite);
        var current = source.Admissions.Where(x => x.Epoch == start + 1 && x.Current).Select(x => x.Key).ToHashSet();
        Assert.NotEmpty(current);
        Assert.Contains(afterWrite, current.Contains);
        Assert.Superset(beforeWrite, afterWrite);

        await PostAsync(host, session, "Second question");
        start = store.Writes;
        var turns = harness.Provider.Turns;
        // The owner withdraws its authority right after the checkpoint write.
        source.Deny = epoch => epoch >= start + 2;

        var events = await host.StreamAsync(session);

        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.Error);
        Assert.DoesNotContain(events, x => x.Kind == AgentStreamEventKind.Completed);
        Assert.Equal(turns, harness.Provider.Turns);
    }

    [Fact]
    public async Task Repeated_snapshots_keep_fingerprints_retries_and_the_paused_continuation_stable_across_a_restart()
    {
        var source = new LedgerSource(() => 0);
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentContextProvider>(source));
        var host = harness.Host;
        var session = await host.CreateAsync();
        foreach (var value in new[] { "repeated", "new-1", "repeated", "new-2" })
        {
            source.Current = value;
            await PostAsync(host, session, $"Question about {value}");
            Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        }
        source.Current = "repeated";
        var retried = await PostAsync(host, session, "mutate the resource", "request-1");
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(await host.StreamAsync(session), x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        var messages = await host.Sessions.ListMessagesAsync(session.Id);
        var paused = (await host.Pauses.FindAsync(session.Id))!;
        var record = (await harness.Store.LoadAsync(session.Id))!;
        // Three distinct snapshots, recorded once each, behind many references.
        Assert.Equal(3, record.Snapshots.Count);
        Assert.True(record.Messages.Sum(x => x.DisclosureDigests?.Count ?? 0) + record.PausedTurn!.DisclosureDigests!.Count > 3 * record.Snapshots.Count);

        host = harness.Restart();

        // Every message, its delivery fingerprint and the pause fingerprint read back byte-identical.
        var recovered = await host.Sessions.ListMessagesAsync(session.Id);
        Assert.Equal(messages.Select(x => AgentInvocationIntegrity.Digest(x)), recovered.Select(x => AgentInvocationIntegrity.Digest(x)));
        var resumed = (await host.Pauses.FindAsync(session.Id))!;
        Assert.Equal(paused.DisclosureHistoryFingerprint, resumed.DisclosureHistoryFingerprint);
        Assert.Equal(AgentInvocationIntegrity.Digest(paused), AgentInvocationIntegrity.Digest(resumed));
        // An identical retry returns the original message; the paused turn continues once and nothing runs twice.
        Assert.Equal(retried.Id, (await PostAsync(host, session, "mutate the resource", "request-1")).Id);
        var turns = harness.Provider.Turns;
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal(turns + 1, harness.Provider.Turns);
        Assert.Equal(1, harness.Module.Effects);
        // The continuation adds exactly one snapshot, its receipt; the restart added none.
        Assert.Equal(4, (await harness.Store.LoadAsync(session.Id))!.Snapshots.Count);
    }

    [Theory]
    [InlineData("snapshot-text", "snapshot_digest_mismatch")]
    [InlineData("snapshot-missing", "snapshot_missing")]
    [InlineData("history-order", "history_fingerprint_mismatch")]
    [InlineData("delivery-fingerprint", "assistant_history_mismatch")]
    [InlineData("pause-fingerprint", "pause_history_mismatch")]
    [InlineData("history-missing", "history_missing")]
    [InlineData("pause-history-missing", "history_missing")]
    [InlineData("decode-not-supported", "unreadable")]
    [InlineData("decode-argument", "unreadable")]
    public async Task Recorded_disclosure_state_that_fails_verification_makes_the_session_unavailable_with_no_effect(string tampering, string reason)
    {
        TamperingRecordStore? store = null;
        var source = new LedgerSource(() => 0);
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)), services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.AddSingleton<IAgentContextProvider>(source);
        });
        var session = await harness.Host.CreateAsync();
        foreach (var value in new[] { "new-1", "new-2" })
        {
            source.Current = value;
            await PostAsync(harness.Host, session, $"Question about {value}");
            Assert.Equal(AgentStreamEventKind.Completed, (await harness.Host.StreamAsync(session))[^1].Kind);
        }
        await PostAsync(harness.Host, session, "mutate the resource");
        Assert.Contains(await harness.Host.StreamAsync(session), x => x.Kind == AgentStreamEventKind.ProposalCreated);
        // Past the review deadline: a readable record would be settled Expired when it is hydrated.
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var host = harness.Restart();
        var turns = harness.Provider.Turns;
        store!.Tamper = record => Tamper(record, tampering);

        var (status, body) = await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id });

        AgentRecoveryApi.AssertFailure(status, body, 409, AgentSessionUnavailableException.Code);
        Assert.Contains(reason, body, StringComparison.Ordinal);
        // No effect: nothing settled or audited, no provider call, and the lease the hydration took was given back.
        var recorded = (await store.LoadUntamperedAsync(session.Id))!;
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, recorded.Proposals.Single().Proposal.Status);
        Assert.Null(recorded.LeaseExpiresAt);
        Assert.Empty(await host.AuditAsync(session));
        Assert.Equal((turns, 0), (harness.Provider.Turns, harness.Module.Dispatches));
        Assert.Equal(409, (await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id })).Status);

        // The same record, read back unchanged, hydrates and settles normally.
        store.Tamper = null;
        Assert.Equal(200, (await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id })).Status);
        Assert.Equal(AgentActionProposalStatus.Expired, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
    }

    /// <summary>
    /// A store that cannot answer right now (a pool timeout, an exhausted retry, a read that kept changing) has not shown
    /// the recorded state to be unreadable. Hydration gives the lease back and keeps the failure's own outcome, so the
    /// owner sees a retryable failure instead of <c>agent.session.unavailable_state</c>, and a later read hydrates.
    /// </summary>
    [Theory]
    [InlineData("transient-invalid-operation")]
    [InlineData("transient-store")]
    public async Task A_transient_store_failure_during_hydration_is_not_unavailable_state_and_leaves_no_trace(string failure)
    {
        TamperingRecordStore? store = null;
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)));
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "hello");
        var host = harness.Restart();
        store!.Tamper = record => Tamper(record, failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => host.Sessions.FindAsync(session.Id));

        Assert.Null(AgentSessionUnavailableException.Find(thrown));
        Assert.IsType(failure == "transient-store" ? typeof(AgentContributionException) : typeof(InvalidOperationException), thrown);
        // The lease this hydration took was given back, and nothing was settled or audited.
        Assert.Null((await store.LoadUntamperedAsync(session.Id))!.LeaseExpiresAt);
        Assert.Empty(await host.AuditAsync(session));

        store.Tamper = null;
        Assert.NotNull(await host.Sessions.FindAsync(session.Id));
    }

    private static AgentSessionRecord Tamper(AgentSessionRecord record, string tampering)
    {
        // Infrastructure failures say nothing about the recorded state itself.
        if (tampering == "transient-invalid-operation")
            throw new InvalidOperationException("Timeout expired before a pooled connection was available.");
        if (tampering == "transient-store")
            throw new AgentContributionException($"Agent session '{record.Session.Id}' kept changing while it was read; nothing was hydrated.");
        var assistant = record.Messages.First(x => x.Message.Role == AgentRole.Assistant && x.DisclosureDigests is { Count: > 1 });
        AgentSessionRecord Replace(AgentMessageRecord changed)
            => record with { Messages = record.Messages.Select(x => x.Message.Id == changed.Message.Id ? changed : x).ToArray() };
        return tampering switch
        {
            "snapshot-text" => record with
            {
                Snapshots = record.Snapshots.Select((x, index) => index == 0 ? x with { Canonical = x.Canonical.Replace(Marker, Marker + "changed-", StringComparison.Ordinal) } : x).ToArray()
            },
            "snapshot-missing" => record with { Snapshots = record.Snapshots.Skip(1).ToArray() },
            "history-order" => Replace(assistant with { DisclosureDigests = assistant.DisclosureDigests!.Reverse().ToArray() }),
            "delivery-fingerprint" => Replace(assistant with { Message = assistant.Message with { DeliveryFingerprint = "changed" } }),
            "pause-fingerprint" => record with { PausedTurn = record.PausedTurn! with { Turn = record.PausedTurn.Turn with { DisclosureHistoryFingerprint = "changed" } } },
            // The fingerprint survives but the history it covers does not.
            "history-missing" => Replace(assistant with { DisclosureDigests = null }),
            "pause-history-missing" => record with { PausedTurn = record.PausedTurn! with { DisclosureDigests = null } },
            // A store whose decoding fails throws the exceptions hydration verifies against, not only JSON failures.
            "decode-not-supported" => throw new NotSupportedException("The recorded payload uses an unsupported encoding."),
            "decode-argument" => throw new ArgumentException("The recorded payload is malformed."),
            _ => throw new ArgumentOutOfRangeException(nameof(tampering))
        };
    }

    /// <summary>Runs <paramref name="turns"/> turns and measures the session after each one.</summary>
    private async Task<IReadOnlyList<Measurement>> MeasureAsync(int turns)
    {
        CountingRecordStore? store = null;
        var source = new LedgerSource(() => store!.Writes);
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)),
            services => services.AddSingleton<IAgentContextProvider>(source));
        var host = harness.Host;
        var session = await host.CreateAsync();
        var measured = new List<Measurement>();
        for (var turn = 1; turn <= turns; turn++)
        {
            // Odd turns disclose a new snapshot; even turns repeat the one snapshot every even turn discloses.
            source.Current = turn % 2 == 0 ? "repeated" : $"new-{turn}";
            await PostAsync(host, session, $"Question {turn}");
            source.Admissions.Clear();
            var events = await host.StreamAsync(session);
            Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
            var record = (await harness.Store.LoadAsync(session.Id))!;
            var passes = source.Admissions.GroupBy(x => x.Epoch).ToArray();
            measured.Add(new(turn,
                record.Messages.Count(x => x.Message.Role == AgentRole.Assistant),
                Occurrences(JsonSerializer.Serialize(record), Marker),
                passes.Max(x => x.Count()),
                passes.Sum(x => x.Count() - x.Select(y => y.Key).Distinct().Count()),
                // One new snapshot per odd turn, plus the repeated one from turn 2 on.
                (turn + 1) / 2 + (turn >= 2 ? 1 : 0),
                record.Snapshots.Count));
        }
        foreach (var item in measured.Where(x => x.Turn % 10 == 0 || x.Turn == 1))
            output.WriteLine($"turn {item.Turn}: checkpoints {item.Checkpoints}, stored copies {item.StoredCopies}, recorded snapshots {item.RecordedSnapshots}, largest pass {item.LargestPass}, repeated in pass {item.RepeatedInPass}");
        return measured;
    }

    private static async Task<AgentMessage> PostAsync(RecoveryHost host, AgentSession session, string text, string? requestId = null)
    {
        var collected = await host.Services.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, SourceId, new Dictionary<string, string>()));
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        return await host.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, text, AgentMessageStatus.Pending, null,
            context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true, RequestId = requestId, DeliveryFingerprint = requestId });
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <param name="Checkpoints">Assistant journal entries recorded so far, each a write-ahead or final checkpoint.</param>
    /// <param name="StoredCopies">Recorded copies of disclosed evidence, counted by its marker in the stored aggregate.</param>
    /// <param name="LargestPass">The most owner admissions in one authorization pass of the latest turn.</param>
    /// <param name="RepeatedInPass">Admissions of the latest turn that repeated an identical request within the same pass.</param>
    /// <param name="DistinctSnapshots">The distinct snapshots disclosed so far.</param>
    /// <param name="RecordedSnapshots">The snapshot records the session holds.</param>
    private sealed record Measurement(int Turn, int Checkpoints, int StoredCopies, int LargestPass, int RepeatedInPass, int DistinctSnapshots,
        int RecordedSnapshots);

    /// <param name="Current">Whether the admission was of the current context rather than of retained history.</param>
    private sealed record Admission(long Epoch, string Key, bool Current = false);

    /// <summary>
    /// The owning module's context source. It admits everything and records each admission with the number of store
    /// writes before it: a write ends an authorization pass, so admissions with the same count belong to one pass.
    /// </summary>
    private sealed class LedgerSource(Func<long> writes) : IAgentContextProvider
    {
        internal string Current { get; set; } = "initial";
        internal List<Admission> Admissions { get; } = [];
        /// <summary>Withdraws the owner's authority for admissions made after the given number of store writes.</summary>
        internal Func<long, bool>? Deny { get; set; }
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["volume.resource"], ["1"], ["resource"]);

        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment(Current)], []));

        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            var epoch = writes();
            lock (Admissions)
                Admissions.Add(new(epoch, AgentInvocationIntegrity.Digest(new { request.Context, request.Results, request.IsHistorical, request.IsReceiptContinuation }),
                    !request.IsHistorical));
            return ValueTask.FromResult(Deny?.Invoke(epoch) == true
                ? new AgentContextContribution([], [new(SourceId, "volume.withdrawn", "The owner withdrew its authority over this evidence.", true)])
                : new AgentContextContribution(request.Context, []));
        }

        private static AgentContextAttachment Attachment(string value)
            => new("evidence", Module, "resource-1", "Owned resource", "volume.resource", AgentContextSensitivity.Internal, "selection",
                "Evidence", new { value = Marker + value }, new Dictionary<string, string>())
            { ModuleId = Module, Target = new(Module, "resource", "resource-1", "revision-1") };
    }

    /// <summary>A forwarding record store whose loads can be altered, as corrupt or incompatible stored state would be.</summary>
    private sealed class TamperingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        public Func<AgentSessionRecord, AgentSessionRecord>? Tamper { get; set; }
        public bool SupportsFencing => inner.SupportsFencing;

        public async Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
            => await inner.LoadAsync(sessionId, cancellationToken) is { } record && Tamper is { } tamper ? tamper(record) : await inner.LoadAsync(sessionId, cancellationToken);

        public Task<AgentSessionRecord?> LoadUntamperedAsync(string sessionId) => inner.LoadAsync(sessionId);

        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default) => inner.WriteAsync(write, cancellationToken);
        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
        public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
    }

    /// <summary>A forwarding record store that counts its writes.</summary>
    private sealed class CountingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private long _writes;

        public long Writes => Interlocked.Read(ref _writes);
        public bool SupportsFencing => inner.SupportsFencing;

        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writes);
            return inner.WriteAsync(write, cancellationToken);
        }

        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
        public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
    }
}
