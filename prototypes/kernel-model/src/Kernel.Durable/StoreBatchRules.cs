using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Reject malformed row mutations before any effect; one row mutation per actor per batch.</summary>
public static class StoreBatchRules
{
    public static void Validate(CommitBatch batch,bool deferUpdateAliases=false,bool deferInsertAliases=false,bool ownedSets=false)
    {
        if (batch.Fences is { } fences) ValidateFences(batch,fences,ownedSets);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var rows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turn in batch.Turns)
        {
            if (turn.Delete)
            {
                if (turn.Expected is not { } expected)
                    throw new StoreException(Failure.Definite,"Delete requires the existing row's exact revision.");
                if (turn.Row is { } marker && (turn.Journal is null || !marker.Retired || marker.Creation < 0 ||
                    marker.Creation == 0 && !CreationProtocol.IsPhysicalAddress(marker.Id) ||
                    marker.Status == InstanceStatus.Running || expected == long.MaxValue ||
                    marker != new InstanceRow(marker.Id,"","",marker.Status,"null",expected+1,null,
                        History:History.Minimal,Creation:marker.Creation,Retired:true)))
                    throw new StoreException(Failure.Definite,"A row-bearing Delete must be a canonical retirement marker.");
            }
            if (turn.Row is { } value) ValidateIdentity(value);
            if (turn.Row is { } row && !StringComparer.Ordinal.Equals(turn.Instance,row.Id) &&
                !(turn.Expected is null?deferInsertAliases:deferUpdateAliases))
                throw new StoreException(Failure.Definite,"Turn and row must name the same physical actor.");
            if (turn.Row is { } mutation && !rows.Add(mutation.Id))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same row twice.");
            if ((turn.Row is not null || turn.Delete) && !changed.Add(turn.Instance))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same actor row twice.");
        }
    }

    /// <summary>
    /// An owned-set batch fences each partition once and names its lowest one positionally. Over several partitions it
    /// needs a store that fences k rows, and each turn's address must embed one of the fenced partitions.
    /// </summary>
    private static void ValidateFences(CommitBatch batch,IReadOnlyList<PartitionFence> fences,bool ownedSets)
    {
        var lowest=fences.MinBy(f=>f.Partition);
        if (lowest is null || fences.Select(f=>f.Partition).Distinct().Count()!=fences.Count ||
            lowest!=new PartitionFence(batch.Partition,batch.Epoch,batch.Position))
            throw new StoreException(Failure.Definite,"An owned batch fences each of its partitions once and names the lowest.");
        if (fences.Count==1) return;
        if (!ownedSets) throw new StoreException(Failure.Definite,"This store fences one partition per commit.");
        var fenced=fences.Select(f=>f.Partition).ToHashSet();
        foreach (var turn in batch.Turns.Where(t => !TicketsOnly(t)))
            if (AddressPartition(turn.Instance) is not { } partition || !fenced.Contains(partition))
                throw new StoreException(Failure.Definite,$"Turn {turn.Instance} is outside the batch's fenced partitions.");
    }

    /// <summary>The owner's turn of no actor that only consumes tickets (meet and publish): the store checks each ticket's partition.</summary>
    public static bool TicketsOnly(TurnCommit turn) => turn.Instance.Length == 0 && turn.Row is null && !turn.Delete && turn.Journal is null && turn.Sent.Count == 0;

    /// <summary>The partition an actor address embeds, or null when it embeds none.</summary>
    public static int? AddressPartition(string id)
    {
        try { return new InstanceRef(id).Partition; }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    public static void ValidateIdentity(InstanceRow row)
    {
        if (row.IdentityMetadataRequired && !row.IdentityOnly)
            throw new StoreException(Failure.Definite,"A terminal metadata protocol requires an identity-only row.");
        if (row is { IdentityOnly:true } identity && (identity.Retired || identity.Status==InstanceStatus.Running || identity.Creation!=0 || identity.Parent is not null ||
            identity.State!="null" || !CreationProtocol.IsPhysicalAddress(identity.Id) || !identity.Id.Contains("/key/",StringComparison.Ordinal)))
            throw new StoreException(Failure.Definite,"Identity-only rows must be state-free ended keyed runs.");
    }

    public static void RequirePhysicalCapability(CommitBatch batch,bool available)
    {
        if (!available && batch.Turns.Any(t=>t.Row is { Retired:true,Creation:0 }))
            throw new StoreException(Failure.Definite,"Store has no physical retirement capability.");
    }
}
