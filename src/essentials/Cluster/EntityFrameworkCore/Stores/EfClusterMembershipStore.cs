using System.Data.Common;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.EntityFrameworkCore.Stores;

/// <summary>
/// The membership table's reads and writes. Every write is a compare-and-set on the row's revision inside one bounded
/// retry loop (spec 183, FR-026; ADR 0074), with no provider SQL; every provider failure leaves here wrapped (FR-034).
/// </summary>
/// <remarks>
/// Each operation runs on a context of its own, opened from <paramref name="scopes"/>, because a member's heartbeat,
/// its publishes and every consumer's reads run concurrently and a context is not thread-safe. A retried attempt first
/// settles a write an earlier attempt may already have committed, since a lost commit looks like a failed one.
/// </remarks>
internal sealed class EfClusterMembershipStore(IServiceScopeFactory scopes)
{
    private const int CleanupBatchSize = 100;

    private static readonly EfWriteRetry Writes = new(
        EfWriteRetry.DefaultMaxAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.Transient,
        attempt => TimeSpan.FromMilliseconds(5 * attempt));

    // A racing join inserts the same current host id, which the unique index refuses: a lost race like any other.
    private static readonly EfWriteRetry Joins = new(
        EfWriteRetry.DefaultMaxAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient,
        attempt => TimeSpan.FromMilliseconds(5 * attempt));

    /// <summary>Every row, in one query, so a read either sees the whole table or fails (FR-012).</summary>
    public Task<IReadOnlyList<StoredMember>> ReadAllAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<StoredMember>>("reading the fleet", async context =>
            (await context.Members.AsNoTracking().ToListAsync(cancellationToken)).Select(StoredMember.From).ToArray(),
            cancellationToken);

    /// <summary>
    /// Joins <paramref name="identity"/> as a new incarnation, unless its host id's current incarnation is still live
    /// on <paramref name="now"/> (FR-004a, FR-004b). The insert and the displacement commit together, and the
    /// displacement is conditioned on the revision the liveness judgement read: an incumbent that renews in between
    /// fails the compare-and-set, and the retry judges it live and refuses.
    /// </summary>
    /// <param name="previous">The incarnation a lapsed member rejoins after. When a later incarnation has displaced it,
    /// another process holds the host id and nothing is written (FR-007).</param>
    public Task<JoinOutcome> JoinAsync(
        ClusterMemberIdentity identity,
        ClusterMemberIdentity? previous,
        string reportJson,
        DateTimeOffset now,
        TimeSpan expiryPeriod,
        TimeSpan skewAllowance,
        CancellationToken cancellationToken) =>
        WriteAsync(Joins, $"joining as {identity}", async context =>
        {
            var sameHost = await context.Members.Where(row => row.HostId == identity.HostId).ToListAsync(cancellationToken);
            if (sameHost.Any(row => row.Incarnation == identity.Incarnation.Value))
                return JoinOutcome.Joined;
            if (previous is not null && sameHost.Any(row => row.Incarnation == previous.Incarnation.Value && row.CurrentHostId is null))
                return JoinOutcome.PreviousDisplaced;

            var current = sameHost.Where(row => row.CurrentHostId is not null).ToArray();
            if (current.Select(StoredMember.From).FirstOrDefault(member => member.IsLive(now, skewAllowance)) is { } incumbent)
                return JoinOutcome.Refused(incumbent);

            foreach (var earlier in current)
            {
                earlier.CurrentHostId = null;
                earlier.Revision = checked(earlier.Revision + 1);
            }

            context.Members.Add(new ClusterMemberEntity
            {
                HostId = identity.HostId,
                Incarnation = identity.Incarnation.Value,
                CurrentHostId = identity.HostId,
                Status = MemberStatus.Joining.ToString(),
                HeartbeatAtUtcTicks = now.UtcTicks,
                ExpiryPeriodTicks = expiryPeriod.Ticks,
                ReportJson = reportJson,
                ReportRevision = 1,
                Revision = 1,
                SchemaVersion = ClusterMembershipEfModule.SchemaVersion
            });
            await context.SaveChangesAsync(cancellationToken);
            return JoinOutcome.Joined;
        }, cancellationToken);

    /// <summary>
    /// Renews this member's own row from the start of a heartbeat, carrying its status forward and republishing its
    /// report if it changed (FR-027). A row that is missing, displaced or left is not renewed.
    /// </summary>
    public Task<OwnWrite> RenewAsync(
        ClusterMemberIdentity identity,
        DateTimeOffset heartbeatAt,
        MemberStatus status,
        string reportJson,
        CancellationToken cancellationToken) =>
        WriteOwnAsync($"renewing {identity}", identity, row =>
        {
            row.HeartbeatAtUtcTicks = heartbeatAt.UtcTicks;
            MoveForward(row, status, heartbeatAt);
            Republish(row, reportJson);
        }, cancellationToken);

    /// <summary>Publishes this member's report (FR-011): once this returns, every later fresh read shows it.</summary>
    public Task<OwnWrite> PublishAsync(ClusterMemberIdentity identity, string reportJson, CancellationToken cancellationToken) =>
        WriteOwnAsync($"publishing the report of {identity}", identity, row => Republish(row, reportJson), cancellationToken);

    /// <summary>Moves this member's status forward, never back (FR-005). Leaving stamps when, for cleanup.</summary>
    public Task<OwnWrite> SetStatusAsync(ClusterMemberIdentity identity, MemberStatus status, DateTimeOffset now, CancellationToken cancellationToken) =>
        WriteOwnAsync($"moving {identity} to {status}", identity, row => MoveForward(row, status, now), cancellationToken);

    /// <summary>
    /// Deletes up to one batch of entries that have been left, or expired, for longer than <paramref name="cleanupPeriod"/>
    /// on <paramref name="now"/> (FR-031). Each deletion is conditioned on the revision the judgement read, so an entry
    /// renewed in between is kept, and one another member already deleted is simply gone. Idempotent.
    /// </summary>
    public Task<int> CleanupAsync(DateTimeOffset now, TimeSpan cleanupPeriod, TimeSpan skewAllowance, CancellationToken cancellationToken) =>
        WriteAsync(Writes, "cleaning up departed members", async context =>
        {
            var cutoff = now.UtcTicks - cleanupPeriod.Ticks;
            var expiredBefore = cutoff - skewAllowance.Ticks;
            var candidates = await context.Members
                .Where(row => row.LeftAtUtcTicks < cutoff || row.HeartbeatAtUtcTicks + row.ExpiryPeriodTicks < expiredBefore)
                .OrderBy(row => row.HeartbeatAtUtcTicks)
                .Take(CleanupBatchSize)
                .ToListAsync(cancellationToken);
            // The table's view of "left" is trusted only from rows this build can interpret; any other row goes by expiry.
            var deletable = candidates
                .Where(row => row.HeartbeatAtUtcTicks + row.ExpiryPeriodTicks < expiredBefore || StoredMember.SaysLeft(row))
                .ToArray();
            context.Members.RemoveRange(deletable);
            await context.SaveChangesAsync(cancellationToken);
            return deletable.Length;
        }, cancellationToken);

    private Task<OwnWrite> WriteOwnAsync(string operation, ClusterMemberIdentity identity, Action<ClusterMemberEntity> apply, CancellationToken cancellationToken) =>
        WriteAsync(Writes, operation, async context =>
        {
            var row = await context.Members.SingleOrDefaultAsync(
                candidate => candidate.HostId == identity.HostId && candidate.Incarnation == identity.Incarnation.Value,
                cancellationToken);
            if (row is null)
                return OwnWrite.Missing;
            if (row.CurrentHostId is null)
                return OwnWrite.Displaced;
            if (StoredMember.SaysLeft(row))
                return OwnWrite.Left;

            apply(row);
            var entry = context.Entry(row);
            var reportChanged = entry.Property(candidate => candidate.ReportJson).IsModified;
            // A write that changes nothing is not made: it would move the revision with nothing to show for it.
            if (entry.Properties.Any(property => property.IsModified))
            {
                row.Revision = checked(row.Revision + 1);
                await context.SaveChangesAsync(cancellationToken);
            }

            return OwnWrite.Written(row.ReportRevision, reportChanged);
        }, cancellationToken);

    private static void MoveForward(ClusterMemberEntity row, MemberStatus status, DateTimeOffset now)
    {
        if (!Enum.TryParse<MemberStatus>(row.Status, out var stored) || status <= stored)
            return;

        row.Status = status.ToString();
        if (status == MemberStatus.Left)
            row.LeftAtUtcTicks = now.UtcTicks;
    }

    private static void Republish(ClusterMemberEntity row, string reportJson)
    {
        if (string.Equals(row.ReportJson, reportJson, StringComparison.Ordinal))
            return;

        row.ReportJson = reportJson;
        row.ReportRevision = checked(row.ReportRevision + 1);
    }

    private Task<T> WriteAsync<T>(EfWriteRetry retry, string operation, Func<ClusterMembershipDbContext, Task<T>> attempt, CancellationToken cancellationToken) =>
        RunAsync(operation, context => retry.RunAsync<T>(
            context,
            async () =>
            {
                context.ChangeTracker.Clear();
                try
                {
                    return await attempt(context);
                }
                finally
                {
                    context.ChangeTracker.Clear();
                }
            },
            lastConflict => throw new ClusterMembershipStoreException(
                $"The membership store could not complete {operation} after {retry.MaxAttempts} bounded attempts.", lastConflict),
            cancellationToken).AsTask(),
            cancellationToken);

    private async Task<T> RunAsync<T>(string operation, Func<ClusterMembershipDbContext, Task<T>> run, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await run(scope.ServiceProvider.GetRequiredService<ClusterMembershipDbContext>());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClusterMembershipStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new ClusterMembershipStoreException($"The membership store failed while {operation}.", exception);
        }
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is DbException or DbUpdateException or TimeoutException ||
        EfRelationalExceptionClassifier.IsWrappedProviderFailure(exception) ||
        // EF raises a connection it could not open, a model it could not build and a store it cannot read as this too.
        exception is InvalidOperationException;
}

/// <summary>What a join attempt found.</summary>
internal sealed record JoinOutcome(StoredMember? Incumbent, bool IsPreviousDisplaced = false)
{
    public static JoinOutcome Joined { get; } = new((StoredMember?)null);

    /// <summary>The rejoining member's previous incarnation was displaced: it must never rejoin, and nothing was written.</summary>
    public static JoinOutcome PreviousDisplaced { get; } = new(null, IsPreviousDisplaced: true);

    /// <summary>The host id's current incarnation is still live: the join is refused, and nothing was written.</summary>
    public static JoinOutcome Refused(StoredMember incumbent) => new(incumbent);
}

/// <summary>What a write to a member's own row found, and, once written, whether its report changed.</summary>
internal sealed record OwnWrite(OwnWriteResult Result, long ReportRevision, bool ReportChanged)
{
    public static OwnWrite Missing { get; } = new(OwnWriteResult.Missing, 0, false);
    public static OwnWrite Displaced { get; } = new(OwnWriteResult.Displaced, 0, false);
    public static OwnWrite Left { get; } = new(OwnWriteResult.Left, 0, false);
    public static OwnWrite Written(long reportRevision, bool reportChanged) => new(OwnWriteResult.Written, reportRevision, reportChanged);
}

internal enum OwnWriteResult
{
    Written,
    Missing,
    Displaced,
    Left
}
