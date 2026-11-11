using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// What the stores of one service provider last saw of one lease on a run's ownership row: a revision at which the
/// row held the lease, and the lease's expiry time and the row's schema stamp at that revision. Revision 0 means the
/// revision is not known.
/// </summary>
internal readonly record struct ExecutionFenceCursor(long Revision, DateTimeOffset ExpiresAt, string SchemaVersion);

/// <summary>
/// The fence cursors of one service provider, one per lease that an <see cref="EfExecutionLivenessStateStore"/>
/// of that provider wrote to an ownership row. A fenced checkpoint commit uses its cursor to bump the row without
/// reading it (<see cref="EfRuntimeCheckpointParticipantStaging.StageExecutionFenceBump"/>).
/// </summary>
/// <remarks>
/// <para>
/// It is a singleton because the lease heartbeat runs in a service scope of its own, apart from the scope the drain
/// commits in. Both have to reach the same cursor.
/// </para>
/// <para>
/// Every cursor ever stored names a revision at which the row held exactly that lease, or revision 0, which means
/// not known: the liveness store records
/// one after its own save of the row with that lease, and a commit records one after its transaction committed. A
/// revision of a row is never used twice, so a cursor is either the row as it is now or a revision the row has left.
/// A cursor that is behind therefore only ever makes the commit's bump match no row. That is why writers do not
/// need to coordinate: of two cursors for one lease the higher revision is kept. A cursor that is lost or removed
/// costs a read of the row. A cursor that is behind costs more: a save that is rolled back, and a second run of
/// the commit that reads the row.
/// </para>
/// <para>
/// Only <see cref="Hold"/> creates an entry, and only the liveness store calls it. A commit changes an entry that
/// exists and never creates one, so a lease written to the table by anything but that store has no cursor.
/// </para>
/// </remarks>
internal sealed class EfExecutionFenceCursors(TimeProvider? timeProvider = null)
{
    /// <summary>How many <see cref="Hold"/> calls pass between two removals of the cursors whose lease time ran out.</summary>
    internal const int SweepInterval = 1024;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Key, ExecutionFenceCursor> _cursors = new();
    private int _holds;

    /// <summary>One lease on one ownership row: the persistence scope and run that name the row, and the lease.</summary>
    internal readonly record struct Key(string Scope, string WorkflowExecutionId, string LeaseId, string OwnerId, long FencingToken)
    {
        public static Key For(string scope, string workflowExecutionId, RuntimeExecutionFence fence) =>
            new(scope, workflowExecutionId, fence.LeaseId, fence.OwnerId, fence.FencingToken);

        public static Key For(string scope, RuntimeExecutionLease lease) =>
            new(scope, lease.WorkflowExecutionId, lease.LeaseId, lease.OwnerId, lease.FencingToken);
    }

    public int Count => _cursors.Count;

    public bool TryGet(Key key, out ExecutionFenceCursor cursor) => _cursors.TryGetValue(key, out cursor);

    /// <summary>Stores a cursor the liveness store recorded, creating the entry when there is none.</summary>
    public void Hold(Key key, ExecutionFenceCursor cursor)
    {
        _cursors.AddOrUpdate(key, cursor, (_, current) => current.Revision >= cursor.Revision ? current : cursor);

        // A lease that is never released leaves its entry behind. Its lease time runs out, and a cursor past its
        // lease time is never used, so removing those bounds the registry without changing any answer.
        if (Interlocked.Increment(ref _holds) % SweepInterval == 0)
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var entry in _cursors)
            {
                if (entry.Value.ExpiresAt <= now)
                    _cursors.TryRemove(entry);
            }
        }
    }

    /// <summary>Stores the cursor a committed checkpoint left, when the lease has an entry and it is not already ahead.</summary>
    public void Advance(Key key, ExecutionFenceCursor cursor)
    {
        while (_cursors.TryGetValue(key, out var current) && current.Revision < cursor.Revision)
        {
            if (_cursors.TryUpdate(key, cursor, current))
                return;
        }
    }

    /// <summary>Marks the revision of a lease's entry as not known, so the next commit reads the row.</summary>
    public void Distrust(Key key)
    {
        while (_cursors.TryGetValue(key, out var current) && current.Revision != 0)
        {
            if (_cursors.TryUpdate(key, current with { Revision = 0 }, current))
                return;
        }
    }

    public void Remove(Key key) => _cursors.TryRemove(key, out _);
}
