using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static async ValueTask<(CommitBatch Batch, QualifiedRootCheckpoint? Root, IReadOnlySet<string>? Refusals)> BindRetentionPoliciesAsync(
        KernelDb db, CommitBatch batch, QualifiedRootCheckpoint? root, uint? marker, CancellationToken token)
    {
        if (!db.UsesRootRetentionPolicy || root is not RootCheckpoints checkpoints ||
            !checkpoints.Commands.Any(command => command is RootRetirement { IsRetentionDelete: true }))
            return (batch, root, null);

        // Lock policy before the fence or any projection write. Activation locks in the same order.
        // SQLite already reserves the writer for the enclosing commit.
        var sqlite = db.UsesSqliteRootRuntime;
        if (!sqlite)
        {
            if (marker is null) throw new StoreFormatException("Retention settlement requires the pinned physical store.");
            await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationRetentionPolicies" IN SHARE MODE""", token);
        }
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
            var window = current.Status switch
            {
                InstanceStatus.Finished => policy.Windows.Finished,
                InstanceStatus.Cancelled => policy.Windows.Cancelled,
                InstanceStatus.Faulted => policy.Windows.Faulted,
                _ => null
            };
            var eligible = retirement.Park is null && current.TerminalAt is > 0 && window is { } finite &&
                current.TerminalAt.Value <= policy.AsOf - finite;
            if (!eligible) refused.Add(retirement.Root.Actor);
            commands.Add(retirement with { RetentionEligible = eligible });
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
