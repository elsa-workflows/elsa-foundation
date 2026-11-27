using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record RetentionAdmissionPolicy(ActorRetentionPolicy Policy, string? EncodedType, long AsOf)
    {
        internal bool Allows(string? header, InstanceStatus status, long endedAt) =>
            EncodedType is not null && header is not null &&
            Elapsed(RetentionMillis.From(Policy.Windows), status, endedAt, AsOf) &&
            EncodedType == EncodeRetentionActorType(DurableKeyJson.Read<RootHeader>(header).Model);
    }

    internal sealed record RetentionAdmissionTarget(string? Header, InstanceStatus Status);

    internal static async Task<RetentionAdmissionPolicy> ReadRetentionAdmissionPolicyAsync(KernelDb db, string tenant,
        long? marker, long? now, CancellationToken token)
    {
        if (db.Database.IsSqlite())
        {
            var key = await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);
            var row = await ReadSqliteRetentionPolicyRowAsync(db, tenant, token);
            return new(DecodeRetentionPolicy(tenant, row?.ActorType ?? "", row, key), row?.ActorType, now!.Value);
        }

        await LockPostgresRetentionPolicyAsync(db, marker, token);
        var stored = await ReadPostgresRetentionPolicyAsync(db, marker!.Value, tenant, token, "ShareLock");
        return new(DecodePostgresRetentionPolicy(tenant, stored.ActorType ?? "", stored), stored.ActorType, stored.AsOf);
    }

    private static Task<int> LockPostgresRetentionPolicyAsync(KernelDb db, long? marker, CancellationToken token)
    {
        if (marker is null) throw new StoreFormatException("Retention requires the pinned physical store.");
        return db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationRetentionPolicies" IN SHARE MODE""", token);
    }

    private static bool Elapsed(RetentionMillis windows, InstanceStatus status, long endedAt, long asOf)
    {
        var window = status switch
        {
            InstanceStatus.Finished => windows.Finished,
            InstanceStatus.Cancelled => windows.Cancelled,
            InstanceStatus.Faulted => windows.Faulted,
            _ => null
        };
        return endedAt > 0 && window is { } finite && endedAt <= asOf - finite;
    }

    private static async ValueTask<(CommitBatch Batch, QualifiedRootCheckpoint? Root, IReadOnlySet<string>? Refusals)> BindRetentionPoliciesAsync(
        KernelDb db, CommitBatch batch, QualifiedRootCheckpoint? root, uint? marker, CancellationToken token)
    {
        if (!db.UsesRootRetentionPolicy || root is not RootCheckpoints checkpoints ||
            !checkpoints.Commands.Any(command => command is RootRetirement { IsRetentionDelete: true }))
            return (batch, root, null);

        // Lock policy before the fence or any projection write. Activation locks in the same order.
        // SQLite already reserves the writer for the enclosing commit.
        var sqlite = db.UsesSqliteRootRuntime;
        if (!sqlite) await LockPostgresRetentionPolicyAsync(db, marker, token);
        var sqliteKey = sqlite ? await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token) : null;
        var now = sqlite ? await ReadSqliteRetentionClockAsync(db, token) : 0L;
        var policies = new Dictionary<(string Tenant, string Model), (RetentionMillis Windows, long AsOf)>();
        var commands = new List<QualifiedRootCheckpoint>(checkpoints.Commands.Count);
        var refused = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in checkpoints.Commands)
        {
            if (command is not RootRetirement { IsRetentionDelete: true } retirement)
            {
                commands.Add(command);
                continue;
            }

            var current = retirement.Current;
            var tenant = retirement.Root.Tenant;
            var identity = (tenant, current.Model);
            if (!policies.TryGetValue(identity, out var policy))
            {
                var type = EncodeRetentionActorType(current.Model);
                if (sqlite)
                {
                    var saved = await ReadSqliteRetentionPolicyAsync(db, tenant, type, sqliteKey!, token);
                    policy = (RetentionMillis.From(saved.Windows), now);
                }
                else
                {
                    var row = await ReadPostgresRetentionPolicyAsync(db, marker!.Value, tenant, token, "ShareLock");
                    policy = (RetentionMillis.From(DecodePostgresRetentionPolicy(tenant, type, row).Windows), row.AsOf);
                }
                policies.Add(identity, policy);
            }
            var eligible = retirement.Park is null && current.TerminalAt is { } endedAt &&
                Elapsed(policy.Windows, current.Status, endedAt, policy.AsOf);
            if (!eligible) refused.Add(retirement.Root.Actor);
            commands.Add(retirement with { RetentionEligible = eligible, RetentionMarker = marker });
        }

        if (refused.Count == 0) return (batch, checkpoints with { Commands = commands }, null);
        batch = batch.WithoutRetentionJournals(refused);
        var positions = batch.Fenced.ToDictionary(fence => fence.Partition, fence => fence.Position);
        var journalPositions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var turn in batch.Turns.Where(turn => turn.Journal is not null))
            journalPositions.Add(turn.Instance, ++positions[batch.PartitionOf(turn)]);
        for (var i = 0; i < commands.Count; i++)
            if (commands[i] is RootRetirement retirement)
                commands[i] = retirement with { Position = journalPositions.GetValueOrDefault(retirement.Root.Actor) };
        return (batch, checkpoints with { Commands = commands }, refused);
    }
}
