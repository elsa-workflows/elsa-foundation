using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Process-wide group-commit coordinator for the shared Groundwork durable writer (spec 115). Concurrent checkpoint
/// commits that would otherwise each open their own unit-of-work and serialize behind the single SQLite writer are
/// folded, at a flush boundary, into one shared <see cref="IDocumentUnitOfWork"/> committed once — one transaction, one
/// fsync — for the whole batch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mechanism (flush-pipeline group commit).</b> Each caller enqueues its commit and waits on a single
/// <see cref="SemaphoreSlim"/> flush gate. Whoever acquires the gate becomes the leader and, <em>while holding the
/// gate</em>, flushes every commit currently queued for the same tenant (up to
/// <see cref="RuntimeGroupCommitOptions.MaxBatchSize"/>) — a multi-member batch through one shared unit-of-work, a lone
/// commit through its own single-commit fallback. Holding the gate across the durable flush is what makes the pattern
/// work: while the leader is committing, other runs that reach their commit point enqueue and block on the gate, so the
/// next leader finds them already waiting and folds them into one transaction. This mirrors a database log mutex, not a
/// timed batch window: there is no artificial wait, and a lone committer (a batch of one) simply performs its own
/// commit under the gate — the same single commit it would do without group commit, so solo latency is unchanged (FR-2).
/// </para>
/// <para>
/// <b>Durability ack (FR-3, ADR 0020).</b> A member's <see cref="RuntimeCheckpointCommitStoreResult"/> is returned only
/// after the shared (or solo) <see cref="IDocumentUnitOfWork.CommitAsync"/> returns, i.e. after the bytes are durably
/// synced. The fsync is shared; the ack is never released before it.
/// </para>
/// <para>
/// <b>Failure isolation (FR-4).</b> The Groundwork unit-of-work is all-or-nothing: one member's optimistic-concurrency
/// conflict (stale fence, create-only marker replay, …) poisons the whole transaction. On any member failure during a
/// batch, the shared unit-of-work is rolled back and every member is re-driven through its own single-commit fallback
/// (today's behavior, including its fence/marker retry loop and marker-idempotent replay reconciliation). No member is
/// lost or half-applied, and one member's failure never fails another.
/// </para>
/// <para>
/// <b>Concurrency model.</b> All member state is mutated only by the leader while holding the gate and read by each
/// member only after it acquires the gate — the semaphore provides the happens-before. The flush gate is always taken
/// before the store's own writer connection gate (only the leader ever holds both, in that order), so the two cannot
/// deadlock.
/// </para>
/// </remarks>
public sealed class RuntimeGroupCommitCoordinator(IDocumentStore store, RuntimeGroupCommitOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<Member> _queue = new();
    private long _batchFlushCount;
    private long _batchedMemberCount;
    private long _soloFlushCount;
    private long _degradedBatchCount;

    /// <summary>Number of shared multi-member transactions committed (deterministic batching evidence).</summary>
    public long BatchFlushCount => Interlocked.Read(ref _batchFlushCount);

    /// <summary>Total members folded into shared transactions. <c>BatchedMemberCount - BatchFlushCount</c> = commits saved.</summary>
    public long BatchedMemberCount => Interlocked.Read(ref _batchedMemberCount);

    /// <summary>Number of flushes that found only one queued commit and committed it through the single-commit fallback.</summary>
    public long SoloFlushCount => Interlocked.Read(ref _soloFlushCount);

    /// <summary>Number of multi-member batches that hit a member failure and re-drove every member individually.</summary>
    public long DegradedBatchCount => Interlocked.Read(ref _degradedBatchCount);

    /// <summary>
    /// Submits one checkpoint commit to the group. <paramref name="stage"/> applies the commit's document writes into a
    /// shared unit-of-work without committing it (used inside a batch); <paramref name="fallback"/> commits the same
    /// checkpoint through its own atomic unit-of-work (used for a solo flush and for individual re-drive after a batch
    /// failure). All batched members must share the same <paramref name="batchKey"/> (tenant), as the unit-of-work
    /// scope resolver forbids mixed-tenant transactions.
    /// </summary>
    public async ValueTask<RuntimeCheckpointCommitStoreResult> SubmitAsync(
        string batchKey,
        DocumentCommitScope scope,
        Func<IDocumentStore, CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> stage,
        Func<CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> fallback,
        CancellationToken cancellationToken)
    {
        var member = new Member(batchKey, stage, fallback);
        _queue.Enqueue(member);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!member.Handled)
                await LeadFlushAsync(member, scope, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        if (member.Failure is { } failure)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return member.Result!;
    }

    private async ValueTask LeadFlushAsync(Member leader, DocumentCommitScope scope, CancellationToken cancellationToken)
    {
        var batch = DrainSameTenant(leader);
        if (batch.Count == 1)
        {
            // Only one commit was waiting: no fsync to share. Commit it through its own single-commit path (the store's
            // full retry/reconciliation loop) here under the gate — holding the gate across this commit is exactly what
            // lets the next arrivals accumulate into a batch. Byte-identical to not having group commit for a lone run.
            Interlocked.Increment(ref _soloFlushCount);
            await CommitViaFallbackAsync(leader, cancellationToken);
            return;
        }

        IDocumentUnitOfWork? unitOfWork = null;
        var poisoned = false;
        try
        {
            unitOfWork = await store.BeginAsync(scope, cancellationToken);
            var transactionalStore = new GroundworkDocumentUnitOfWorkStore(store, unitOfWork);
            foreach (var member in batch)
                member.Result = await member.Stage(transactionalStore, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            // The shared unit-of-work is poisoned by any member's failure; roll it back (dispose) and degrade the whole
            // batch to individual re-drive so no member is lost and each keeps its own atomic outcome (FR-4).
            poisoned = true;
        }
        finally
        {
            if (unitOfWork is not null)
                await unitOfWork.DisposeAsync();
        }

        if (poisoned)
        {
            Interlocked.Increment(ref _degradedBatchCount);
            foreach (var member in batch)
            {
                member.Result = null;
                member.Failure = null;
                await CommitViaFallbackAsync(member, cancellationToken);
            }
            return;
        }

        Interlocked.Increment(ref _batchFlushCount);
        Interlocked.Add(ref _batchedMemberCount, batch.Count);
        foreach (var member in batch)
            member.Handled = true;
    }

    private static async ValueTask CommitViaFallbackAsync(Member member, CancellationToken cancellationToken)
    {
        try
        {
            member.Result = await member.Fallback(cancellationToken);
        }
        catch (Exception exception)
        {
            member.Failure = exception;
        }
        finally
        {
            member.Handled = true;
        }
    }

    // Removes the leader and every same-tenant queued commit (up to the max batch size) from the queue and returns them
    // as the batch. Different-tenant and overflow members are requeued for a subsequent leader. Draining the leader out
    // of the queue by reference guarantees it is flushed exactly once — never left behind to be re-processed later.
    private List<Member> DrainSameTenant(Member leader)
    {
        var batch = new List<Member> { leader };
        List<Member>? deferred = null;
        while (_queue.TryDequeue(out var member))
        {
            if (ReferenceEquals(member, leader))
                continue;
            if (batch.Count < options.MaxBatchSize && string.Equals(member.BatchKey, leader.BatchKey, StringComparison.Ordinal))
                batch.Add(member);
            else
                (deferred ??= []).Add(member);
        }

        if (deferred is not null)
            foreach (var member in deferred)
                _queue.Enqueue(member);

        return batch;
    }

    private sealed class Member(
        string batchKey,
        Func<IDocumentStore, CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> stage,
        Func<CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> fallback)
    {
        public string BatchKey { get; } = batchKey;
        public Func<IDocumentStore, CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> Stage { get; } = stage;
        public Func<CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> Fallback { get; } = fallback;
        public RuntimeCheckpointCommitStoreResult? Result { get; set; }
        public Exception? Failure { get; set; }
        public bool Handled { get; set; }
    }
}
