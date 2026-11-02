using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Elsa.Agent.Tests;

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

    [Fact]
    public async Task Stored_snapshots_and_owner_admissions_per_pass_grow_quadratically_with_checkpoints()
    {
        var measured = await MeasureAsync(Turns);

        // Each turn writes one checkpoint and one final journal; every one of them stores the complete cumulative list.
        Assert.True(measured[^1].Checkpoints >= 40);
        var half = measured[Turns / 2 - 1];
        var full = measured[^1];
        // Quadratic: the second half of the session adds more than twice what the first half added.
        Assert.True(full.StoredCopies - half.StoredCopies > 2 * half.StoredCopies,
            $"stored copies {half.StoredCopies} -> {full.StoredCopies}");
        // One pass re-admits every stored copy, so doubling the session more than triples its largest pass.
        Assert.True(full.LargestPass > 3 * half.LargestPass, $"largest pass {half.LargestPass} -> {full.LargestPass}");
        // And within one pass the owner is asked to admit the same snapshot again and again.
        Assert.True(full.RepeatedInPass > full.DistinctSnapshots, $"repeated {full.RepeatedInPass}, distinct {full.DistinctSnapshots}");
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
                record.Messages.Count(x => x.Role == AgentRole.Assistant),
                Occurrences(JsonSerializer.Serialize(record), Marker),
                passes.Max(x => x.Count()),
                passes.Sum(x => x.Count() - x.Select(y => y.Key).Distinct().Count()),
                turn / 2 + 1));
        }
        foreach (var item in measured.Where(x => x.Turn % 10 == 0 || x.Turn == 1))
            output.WriteLine($"turn {item.Turn}: checkpoints {item.Checkpoints}, stored copies {item.StoredCopies}, largest pass {item.LargestPass}, repeated in pass {item.RepeatedInPass}");
        return measured;
    }

    private static async Task PostAsync(RecoveryHost host, AgentSession session, string text)
    {
        var collected = await host.Services.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, SourceId, new Dictionary<string, string>()));
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        await host.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, text, AgentMessageStatus.Pending, null,
            context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true });
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
    private sealed record Measurement(int Turn, int Checkpoints, int StoredCopies, int LargestPass, int RepeatedInPass, int DistinctSnapshots);

    private sealed record Admission(long Epoch, string Key);

    /// <summary>
    /// The owning module's context source. It admits everything and records each admission with the number of store
    /// writes before it: a write ends an authorization pass, so admissions with the same count belong to one pass.
    /// </summary>
    private sealed class LedgerSource(Func<long> writes) : IAgentContextProvider
    {
        internal string Current { get; set; } = "initial";
        internal List<Admission> Admissions { get; } = [];
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["volume.resource"], ["1"], ["resource"]);

        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment(Current)], []));

        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            lock (Admissions)
                Admissions.Add(new(writes(), AgentInvocationIntegrity.Digest(new { request.Context, request.Results, request.IsHistorical, request.IsReceiptContinuation })));
            return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
        }

        private static AgentContextAttachment Attachment(string value)
            => new("evidence", Module, "resource-1", "Owned resource", "volume.resource", AgentContextSensitivity.Internal, "selection",
                "Evidence", new { value = Marker + value }, new Dictionary<string, string>())
            { ModuleId = Module, Target = new(Module, "resource", "resource-1", "revision-1") };
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
