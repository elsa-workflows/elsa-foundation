using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The workflow runs a kernel host's foundation holds, for a kernel shell's run reads (Studio's Runs page): the
/// foundation's reads and the one tenant the host binds them to. A host composes it and registers it by instance, as it
/// does <see cref="KernelServing"/>, so each shell resolves the host's own. A caller sees these runs only in that tenant.
/// </summary>
public sealed class KernelRuns
{
    private readonly Func<CancellationToken, ValueTask<IActorReads>> _reads;

    /// <param name="tenant">The tenant the host's foundation binds: its runs are this tenant's.</param>
    /// <param name="reads">The foundation's reads, opened when first needed.</param>
    public KernelRuns(BoundScope tenant, Func<CancellationToken, ValueTask<IActorReads>> reads)
    {
        Tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
    }

    /// <summary>The tenant the host's foundation binds.</summary>
    public BoundScope Tenant { get; }

    /// <summary>Reads the committed workflow run with this exact reference. A non-workflow actor still occupies the reference.</summary>
    public async ValueTask<(bool Missing, (ActorView View, Kernel.WorkflowActor.WorkflowRunHeader Run)? Run)> GetAsync(ActorRef actor,
        CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        var view = await reads.GetAsync(Tenant, actor, cancellationToken);
        if (view is null)
            return (true, null);
        if (view.Header.Ref != actor || view.Header.Tenant != Tenant.Tenant ||
            Kernel.WorkflowActor.WorkflowActor.DescribeRun(view.Header) is not { } run)
            return (false, null);
        return (false, (view, run));
    }

    /// <summary>Reads committed output from this exact end in the host's tenant.</summary>
    public async ValueTask<ActorTerminalOutput?> ReadTerminalOutputAsync(ActorRef actor, long endedAt, bool includeValues,
        CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        return reads is IActorTerminalOutputReads output
            ? await output.ReadTerminalOutputAsync(Tenant, actor, endedAt, includeValues, cancellationToken)
            : null;
    }

    /// <summary>Reads a committed journal page for this run in the host's tenant.</summary>
    public async ValueTask<Page<JournalEntry>> TailAsync(ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        return await reads.TailAsync(Tenant, actor, from, cancellationToken);
    }

    /// <summary>Whether this host's selected foundation supports sequenced journal reads.</summary>
    public async ValueTask<bool> SupportsSequencedJournalAsync(CancellationToken cancellationToken = default) =>
        await _reads(cancellationToken) is IActorSequencedJournalReads { SupportsSequencedJournal: true };

    /// <summary>Whether the selected foundation carries indexed playback.</summary>
    public async ValueTask<bool> SupportsPlaybackAsync(CancellationToken cancellationToken = default) =>
        await _reads(cancellationToken) is IActorPlaybackReads { SupportsPlayback: true };

    /// <summary>Whether the selected foundation indexes descendant membership.</summary>
    public async ValueTask<bool> SupportsDescendantPlaybackAsync(CancellationToken cancellationToken = default) =>
        await _reads(cancellationToken) is IActorPlaybackReads { SupportsDescendantPlayback: true };

    /// <summary>Reads a ranked occurrence window in the host's tenant.</summary>
    public async ValueTask<ActorPlaybackPage?> ReadPlaybackAsync(ActorRef actor, ActorPlaybackQuery query,
        CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        return reads is IActorPlaybackReads { SupportsPlayback: true } playback
            ? await playback.ReadPlaybackAsync(Tenant, actor, query, cancellationToken)
            : throw new NotSupportedException("The configured foundation does not expose indexed playback.");
    }

    /// <summary>Reads a bounded retained sequence window in the host's bound tenant.</summary>
    public async ValueTask<ActorSequencedJournalPage?> ReadSequencedJournalAsync(ActorRef actor, long afterSequence,
        long? upperSequence, int limit, CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        return reads is IActorSequencedJournalReads { SupportsSequencedJournal: true } sequenced
            ? await sequenced.ReadSequencedJournalAsync(Tenant, actor, afterSequence, upperSequence, limit, cancellationToken)
            : throw new NotSupportedException("The configured foundation does not expose a sequenced journal.");
    }

    /// <summary>
    /// The workflow runs of the host's tenant, one page after another until the last, filtered by definition when one is
    /// given. Each is the actor's view and what its header says about the run (<see cref="Kernel.WorkflowActor.WorkflowActor.DescribeRun"/>).
    /// </summary>
    public async IAsyncEnumerable<(ActorView View, Kernel.WorkflowActor.WorkflowRunHeader Run)> ListAsync(string? definition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        var query = new ActorQuery(Kernel.WorkflowActor.WorkflowActor.ActorType,
            Labels: definition is null ? null : new Dictionary<string, string> { ["definition"] = definition });
        PageToken? from = null;
        do
        {
            var page = await reads.ListAsync(Tenant, query, from, cancellationToken);
            foreach (var view in page.Items)
                if (Kernel.WorkflowActor.WorkflowActor.DescribeRun(view.Header) is { } run)
                    yield return (view, run);
            from = page.Next;
        } while (from is not null);
    }
}
